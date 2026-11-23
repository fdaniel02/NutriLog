using Microsoft.EntityFrameworkCore;
using NutriLog.Data;

namespace NutriLog.Features.Recipes;

public interface IRecipes
{
    // All recipes by name when the query is empty.
    Task<IReadOnlyList<RecipeMatch>> SearchAsync(string? query, CancellationToken cancellationToken = default);
    Task<RecipeDetails?> GetAsync(int recipeId, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(RecipeInput recipe, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(int recipeId, RecipeInput recipe, CancellationToken cancellationToken = default);
    Task<RecipeDeleteResult> DeleteAsync(int recipeId, CancellationToken cancellationToken = default);
    Task<int> LogAsync(DateOnly date, TimeOnly timeEaten, int recipeId, double servings, CancellationToken cancellationToken = default);
    Task<RecipeEntryDetails?> GetEntryAsync(int entryId, CancellationToken cancellationToken = default);
    // Keeps the nutrients the entry was logged with.
    Task<bool> UpdateEntryAsync(int entryId, TimeOnly timeEaten, double servings, CancellationToken cancellationToken = default);
}

public sealed class Recipes(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time) : IRecipes
{
    public const int MaxResults = 30;

    public async Task<IReadOnlyList<RecipeMatch>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Recipe> recipes = db.Recipes;
        foreach (var word in (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pattern = "%" + Escape(word) + "%";
            recipes = recipes.Where(r => EF.Functions.ILike(r.Name, pattern, EscapeCharacter));
        }
        var found = await recipes.OrderBy(r => r.Name).Take(MaxResults).Select(r => r.Id).ToListAsync(cancellationToken);
        var details = await LoadAsync(db, found, cancellationToken);
        return found.Select(id => details[id])
            .Select(r => new RecipeMatch(r.Id, r.Name, r.Servings, r.PerServing[Data.Nutrients.Energy]))
            .ToList();
    }

    // Npgsql sends ILIKE without an escape character unless given one.
    const string EscapeCharacter = @"\";

    static string Escape(string text) => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    public async Task<RecipeDetails?> GetAsync(int recipeId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return (await LoadAsync(db, [recipeId], cancellationToken)).GetValueOrDefault(recipeId);
    }

    public async Task<int> CreateAsync(RecipeInput recipe, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var (name, ingredients) = await ValidateAsync(db, recipe, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var entity = new Recipe { Name = name, Servings = recipe.Servings, CreatedUtc = now, UpdatedUtc = now };
        entity.Ingredients.AddRange(ingredients);
        db.Recipes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(int recipeId, RecipeInput recipe, CancellationToken cancellationToken = default)
    {
        await using var strategyContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        // The pages' DbContext retries on failure, which only works with the whole transaction inside the strategy,
        // on a fresh DbContext per attempt.
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var entity = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId, ct);
            if (entity is null)
                return false;
            var (name, ingredients) = await ValidateAsync(db, recipe, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.RecipeIngredients.Where(i => i.RecipeId == recipeId).ExecuteDeleteAsync(ct);
            entity.Name = name;
            entity.Servings = recipe.Servings;
            entity.UpdatedUtc = time.GetUtcNow().UtcDateTime;
            db.RecipeIngredients.AddRange(ingredients.Select(i => new RecipeIngredient { RecipeId = recipeId, FoodId = i.FoodId, Grams = i.Grams }));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }, cancellationToken);
    }

    public async Task<RecipeDeleteResult> DeleteAsync(int recipeId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.DiaryEntries.AnyAsync(e => e.RecipeId == recipeId, cancellationToken))
            return RecipeDeleteResult.InDiary;
        return await db.Recipes.Where(r => r.Id == recipeId).ExecuteDeleteAsync(cancellationToken) > 0
            ? RecipeDeleteResult.Deleted
            : RecipeDeleteResult.NotFound;
    }

    public async Task<int> LogAsync(DateOnly date, TimeOnly timeEaten, int recipeId, double servings, CancellationToken cancellationToken = default)
    {
        ValidateServings(servings);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await SnapshotsAsync(db, [recipeId], cancellationToken);
        if (!snapshots.TryGetValue(recipeId, out var snapshot))
            throw new ArgumentException($"Recipe {recipeId} doesn't exist.", nameof(recipeId));
        var entry = NewEntry(date, timeEaten, recipeId, servings, snapshot, time.GetUtcNow().UtcDateTime);
        db.DiaryEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return entry.Id;
    }

    public async Task<RecipeEntryDetails?> GetEntryAsync(int entryId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entry = await db.DiaryEntries.AsNoTracking().Include(e => e.Nutrients)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.RecipeId != null, cancellationToken);
        if (entry is null)
            return null;
        var name = await db.Recipes.Where(r => r.Id == entry.RecipeId).Select(r => r.Name).SingleAsync(cancellationToken);
        return new RecipeEntryDetails(entry.Id, entry.Date, entry.TimeEaten, entry.RecipeId!.Value, name, entry.Quantity, PerServing(entry));
    }

    public async Task<bool> UpdateEntryAsync(int entryId, TimeOnly timeEaten, double servings, CancellationToken cancellationToken = default)
    {
        ValidateServings(servings);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entry = await db.DiaryEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.RecipeId != null, cancellationToken);
        if (entry is null)
            return false;
        entry.TimeEaten = timeEaten;
        entry.Quantity = servings;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static IReadOnlyDictionary<string, (double PerServing, bool Complete)> PerServing(DiaryEntry entry) =>
        entry.Nutrients.ToDictionary(n => n.NutrientId, n => (n.PerServing, n.Complete));

    public static DiaryEntry NewEntry(DateOnly date, TimeOnly timeEaten, int recipeId, double servings,
        IReadOnlyDictionary<string, (double PerServing, bool Complete)> snapshot, DateTime createdUtc)
    {
        var entry = new DiaryEntry
        {
            Date = date,
            TimeEaten = timeEaten,
            RecipeId = recipeId,
            Quantity = servings,
            QuantityUnit = QuantityUnit.Servings,
            CreatedUtc = createdUtc
        };
        entry.Nutrients.AddRange(snapshot.Select(n => new DiaryEntryNutrient { NutrientId = n.Key, PerServing = n.Value.PerServing, Complete = n.Value.Complete }));
        return entry;
    }

    // The recipes' nutrients per serving as they are now, for new entries.
    public static async Task<Dictionary<int, IReadOnlyDictionary<string, (double PerServing, bool Complete)>>> SnapshotsAsync(
        AppDbContext db, IReadOnlyCollection<int> recipeIds, CancellationToken cancellationToken) =>
        (await LoadAsync(db, recipeIds, cancellationToken)).ToDictionary(r => r.Key,
            r => RecipeNutrients.Snapshot(r.Value.Ingredients.Select(i => (i.Per100, i.Grams)), r.Value.Servings));

    static async Task<Dictionary<int, RecipeDetails>> LoadAsync(AppDbContext db, IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        var recipes = await db.Recipes.AsNoTracking().Where(r => ids.Contains(r.Id)).Include(r => r.Ingredients).ToListAsync(cancellationToken);
        var foodIds = recipes.SelectMany(r => r.Ingredients).Select(i => i.FoodId).Distinct().ToList();
        var foods = await db.Foods.AsNoTracking().Where(f => foodIds.Contains(f.Id)).Include(f => f.Nutrients)
            .ToDictionaryAsync(f => f.Id, cancellationToken);
        return recipes.ToDictionary(r => r.Id, r => new RecipeDetails(r.Id, r.Name, r.Servings,
            r.Ingredients
                .Select(i => Ingredient(foods[i.FoodId], i.Grams))
                .OrderBy(i => i.Name).ToList()));
    }

    static RecipeIngredientDetails Ingredient(Food food, double grams) =>
        new(food.Id, food.Name, food.Basis, grams, food.Nutrients.ToDictionary(n => n.NutrientId, n => n.Amount));

    static async Task<(string Name, List<RecipeIngredient> Ingredients)> ValidateAsync(AppDbContext db, RecipeInput recipe, CancellationToken cancellationToken)
    {
        var name = recipe.Name.Trim();
        if (name.Length is 0 or > AppDbContext.MaxNameLength)
            throw new InvalidRecipeException($"The name must be 1 to {AppDbContext.MaxNameLength} characters.");
        if (!double.IsFinite(recipe.Servings) || recipe.Servings <= 0)
            throw new InvalidRecipeException("The number of servings must be more than 0.");
        if (recipe.Ingredients.Count == 0)
            throw new InvalidRecipeException("Add at least one food.");
        if (recipe.Ingredients.Any(i => !double.IsFinite(i.Grams) || i.Grams <= 0))
            throw new InvalidRecipeException("Each food needs an amount above 0.");
        // A food listed twice is one ingredient with both amounts; the table has one row per food.
        var ingredients = recipe.Ingredients.GroupBy(i => i.FoodId)
            .Select(g => new RecipeIngredient { FoodId = g.Key, Grams = g.Sum(i => i.Grams) })
            .ToList();
        var ids = ingredients.Select(i => i.FoodId).ToList();
        if (await db.Foods.CountAsync(f => ids.Contains(f.Id), cancellationToken) != ids.Count)
            throw new InvalidRecipeException("One of the foods doesn't exist.");
        return (name, ingredients);
    }

    static void ValidateServings(double servings)
    {
        if (!double.IsFinite(servings) || servings <= 0)
            throw new ArgumentException("The number of servings must be more than 0.", nameof(servings));
    }
}
