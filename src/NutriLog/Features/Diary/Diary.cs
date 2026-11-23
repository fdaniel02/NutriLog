using Microsoft.EntityFrameworkCore;
using NutriLog.Data;

namespace NutriLog.Features.Diary;

public interface IDiary
{
    Task<DiaryDay> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default);
    // Foods used in the last weeks first, most often used first; an empty query returns only those.
    Task<IReadOnlyList<FoodMatch>> SearchFoodsAsync(string? query, CancellationToken cancellationToken = default);
    Task<FoodChoice?> GetFoodAsync(int foodId, CancellationToken cancellationToken = default);
    Task<int> AddEntryAsync(DateOnly date, Meal meal, TimeOnly timeEaten, int foodId, EntryAmount amount, CancellationToken cancellationToken = default);
    Task<EntryDetails?> GetEntryAsync(int entryId, CancellationToken cancellationToken = default);
    Task<bool> UpdateEntryAsync(int entryId, Meal meal, TimeOnly timeEaten, EntryAmount amount, CancellationToken cancellationToken = default);
    Task<bool> DeleteEntryAsync(int entryId, CancellationToken cancellationToken = default);
    // Copies one meal, or the whole day when meal is null, into the same meals on another day.
    Task<int> CopyAsync(DateOnly from, DateOnly to, Meal? meal, CancellationToken cancellationToken = default);
}

public sealed class Diary(IDbContextFactory<AppDbContext> dbFactory, Clock clock, TimeProvider time) : IDiary
{
    public const int RecentDays = 90;
    public const int MaxResults = 30;

    public async Task<DiaryDay> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entries = await db.DiaryEntries.AsNoTracking()
            .Where(e => e.Date == date)
            .Include(e => e.Nutrients)
            .OrderBy(e => e.TimeEaten).ThenBy(e => e.CreatedUtc).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
        var foods = await LoadFoodsAsync(db, entries.Where(e => e.FoodId != null).Select(e => e.FoodId!.Value).Distinct().ToList(), cancellationToken);
        var recipeIds = entries.Where(e => e.RecipeId != null).Select(e => e.RecipeId!.Value).Distinct().ToList();
        var recipeNames = await db.Recipes.Where(r => recipeIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var items = entries.Select(e =>
        {
            if (e.RecipeId is { } recipeId)
                return new DiaryItem(e.Id, e.Meal, e.TimeEaten, recipeNames[recipeId], NutrientFormat.Servings(e.Quantity),
                    NutrientTotals.ForServings(Recipes.Recipes.PerServing(e), e.Quantity));
            var food = foods[e.FoodId!.Value];
            var amount = AmountOf(e);
            return new DiaryItem(e.Id, e.Meal, e.TimeEaten, food.Name, food.Describe(amount), food.Nutrients(amount));
        }).ToList();
        var meals = Enum.GetValues<Meal>().Select(meal =>
        {
            var inMeal = items.Where(i => i.Meal == meal).ToList();
            return new MealGroup(meal, inMeal, NutrientTotals.Sum(inMeal.Select(i => i.Nutrients)));
        }).ToList();
        return new DiaryDay(date, meals, NutrientTotals.Sum(items.Select(i => i.Nutrients)));
    }

    public async Task<IReadOnlyList<FoodMatch>> SearchFoodsAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var since = clock.Today.AddDays(-RecentDays);
        var usage = (await db.DiaryEntries
                .Where(e => e.FoodId != null && e.Date >= since)
                .GroupBy(e => e.FoodId!.Value)
                .Select(g => new { FoodId = g.Key, Count = g.Count(), Last = g.Max(e => e.CreatedUtc) })
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.FoodId);
        var usedIds = usage.Keys.ToList();

        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var recent = (await ToMatches(Matching(db.Foods.Where(f => usedIds.Contains(f.Id)), words)).ToListAsync(cancellationToken))
            .OrderByDescending(f => usage[f.Id].Count).ThenByDescending(f => usage[f.Id].Last)
            .Take(MaxResults)
            .Select(f => f with { Recent = true })
            .ToList();
        if (words.Length == 0)
            return recent;

        var prefix = Escape(string.Join(' ', words)) + "%";
        var others = await ToMatches(Matching(db.Foods.Where(f => !usedIds.Contains(f.Id)), words)
                .OrderBy(f => !EF.Functions.ILike(f.Name, prefix, EscapeCharacter))
                .ThenBy(f => f.Name.Length).ThenBy(f => f.Name)
                .Take(MaxResults - recent.Count))
            .ToListAsync(cancellationToken);
        return [.. recent, .. others];
    }

    static IQueryable<Food> Matching(IQueryable<Food> foods, string[] words)
    {
        foreach (var word in words)
        {
            var pattern = "%" + Escape(word) + "%";
            foods = foods.Where(f => EF.Functions.ILike(f.Name, pattern, EscapeCharacter) || (f.NameNl != null && EF.Functions.ILike(f.NameNl, pattern, EscapeCharacter)));
        }
        return foods;
    }

    static IQueryable<FoodMatch> ToMatches(IQueryable<Food> foods) =>
        foods.Select(f => new FoodMatch(f.Id, f.Name, f.NameNl, f.Source, f.Basis,
            f.Nutrients.Where(n => n.NutrientId == Data.Nutrients.Energy).Select(n => (double?)n.Amount).FirstOrDefault(), false));

    // Npgsql sends ILIKE without an escape character unless given one.
    const string EscapeCharacter = @"\";

    static string Escape(string text) => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    public async Task<FoodChoice?> GetFoodAsync(int foodId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var foods = await LoadFoodsAsync(db, [foodId], cancellationToken);
        if (!foods.TryGetValue(foodId, out var food))
            return null;
        var last = await db.DiaryEntries.AsNoTracking()
            .Where(e => e.FoodId == foodId)
            .OrderByDescending(e => e.CreatedUtc).ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return last is null ? food : food with { LastAmount = AmountOf(last) };
    }

    public async Task<int> AddEntryAsync(DateOnly date, Meal meal, TimeOnly timeEaten, int foodId, EntryAmount amount, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await ValidateAsync(db, foodId, amount, cancellationToken);
        var entry = new DiaryEntry { Date = date, Meal = meal, TimeEaten = timeEaten, FoodId = foodId, CreatedUtc = time.GetUtcNow().UtcDateTime };
        Apply(entry, amount);
        db.DiaryEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return entry.Id;
    }

    public async Task<EntryDetails?> GetEntryAsync(int entryId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entry = await db.DiaryEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entryId && e.FoodId != null, cancellationToken);
        if (entry is null)
            return null;
        var foodId = entry.FoodId!.Value;
        var foods = await LoadFoodsAsync(db, [foodId], cancellationToken);
        return new EntryDetails(entry.Id, entry.Date, entry.Meal, entry.TimeEaten, foods[foodId], AmountOf(entry));
    }

    public async Task<bool> UpdateEntryAsync(int entryId, Meal meal, TimeOnly timeEaten, EntryAmount amount, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entry = await db.DiaryEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.FoodId != null, cancellationToken);
        if (entry is null)
            return false;
        await ValidateAsync(db, entry.FoodId!.Value, amount, cancellationToken);
        entry.Meal = meal;
        entry.TimeEaten = timeEaten;
        Apply(entry, amount);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteEntryAsync(int entryId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DiaryEntries.Where(e => e.Id == entryId).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<int> CopyAsync(DateOnly from, DateOnly to, Meal? meal, CancellationToken cancellationToken = default)
    {
        if (from == to)
            throw new ArgumentException("Copy from another day than the one copied to.", nameof(from));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.DiaryEntries.AsNoTracking()
            .Where(e => e.Date == from && (meal == null || e.Meal == meal))
            .OrderBy(e => e.CreatedUtc).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        // A copy is a new entry, so a recipe counts as it is now, not as it was on the day copied from.
        var snapshots = await Recipes.Recipes.SnapshotsAsync(db,
            source.Where(e => e.RecipeId != null).Select(e => e.RecipeId!.Value).Distinct().ToList(), cancellationToken);
        db.DiaryEntries.AddRange(source.Select(e => e.RecipeId is { } recipeId
            ? Recipes.Recipes.NewEntry(to, e.Meal, e.TimeEaten, recipeId, e.Quantity, snapshots[recipeId], now)
            : new DiaryEntry
            {
                Date = to,
                Meal = e.Meal,
                TimeEaten = e.TimeEaten,
                FoodId = e.FoodId,
                Quantity = e.Quantity,
                QuantityUnit = e.QuantityUnit,
                PortionId = e.PortionId,
                CreatedUtc = now
            }));
        await db.SaveChangesAsync(cancellationToken);
        return source.Count;
    }

    static async Task ValidateAsync(AppDbContext db, int foodId, EntryAmount amount, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(amount.Quantity) || amount.Quantity <= 0)
            throw new ArgumentException("The amount must be more than 0.", nameof(amount));
        if (!await db.Foods.AnyAsync(f => f.Id == foodId, cancellationToken))
            throw new ArgumentException($"Food {foodId} doesn't exist.", nameof(foodId));
        if (amount.PortionId is { } portionId &&
            !await db.Portions.AnyAsync(p => p.Id == portionId && p.FoodId == foodId, cancellationToken))
            throw new ArgumentException($"Portion {portionId} isn't one of food {foodId}'s.", nameof(amount));
    }

    static void Apply(DiaryEntry entry, EntryAmount amount)
    {
        entry.Quantity = amount.Quantity;
        entry.PortionId = amount.PortionId;
        entry.QuantityUnit = amount.PortionId is null ? QuantityUnit.Grams : QuantityUnit.Portion;
    }

    static EntryAmount AmountOf(DiaryEntry entry) =>
        new(entry.Quantity, entry.QuantityUnit == QuantityUnit.Portion ? entry.PortionId : null);

    static async Task<Dictionary<int, FoodChoice>> LoadFoodsAsync(AppDbContext db, List<int> ids, CancellationToken cancellationToken)
    {
        var foods = await db.Foods.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Include(f => f.Nutrients)
            .Include(f => f.Portions)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return foods.ToDictionary(f => f.Id, f => new FoodChoice(f.Id, f.Name, f.Basis, f.Source,
            f.Portions.OrderBy(p => p.Grams).ThenBy(p => p.Name).Select(p => new PortionChoice(p.Id, p.Name, p.Grams)).ToList(),
            f.Nutrients.ToDictionary(n => n.NutrientId, n => n.Amount),
            null));
    }
}
