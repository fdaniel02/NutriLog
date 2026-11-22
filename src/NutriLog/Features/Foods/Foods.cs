using Microsoft.EntityFrameworkCore;
using NutriLog.Data;
using NutriLog.Features.Barcode;

namespace NutriLog.Features.Foods;

public sealed record NewPortion(string Name, double Grams);

// Per100 holds only the nutrients whose value is known; a missing one stays unknown, not 0.
public sealed record NewFood(
    string Name, FoodBasis Basis, IReadOnlyDictionary<string, double> Per100, IReadOnlyList<NewPortion> Portions, string? Barcode = null);

public sealed record EditableFood(
    int Id, string Name, FoodBasis Basis, FoodSource Source, string? Barcode, IReadOnlyDictionary<string, double> Per100, IReadOnlyList<NewPortion> Portions);

public sealed class InvalidFoodException(string message) : Exception(message);

public interface IFoods
{
    Task<int> CreateOwnAsync(NewFood food, CancellationToken cancellationToken = default);
    // Null for a food that doesn't exist or comes from an import, which the next import would overwrite.
    Task<EditableFood?> GetEditableAsync(int foodId, CancellationToken cancellationToken = default);
    // Replaces the name, basis and values; food.Portions are added to the ones it has. False if it can't be edited.
    Task<bool> UpdateAsync(int foodId, NewFood food, CancellationToken cancellationToken = default);
}

public sealed class Foods(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time) : IFoods
{
    static readonly HashSet<string> NutrientIds = [.. Data.Nutrients.All.Select(n => n.Id)];

    public static bool IsEditable(FoodSource source) => source is FoodSource.Own or FoodSource.Off;

    public async Task<int> CreateOwnAsync(NewFood food, CancellationToken cancellationToken = default)
    {
        var name = Validate(food);
        var now = time.GetUtcNow().UtcDateTime;
        var entity = new Food
        {
            Name = name, Source = FoodSource.Own, Basis = food.Basis, Barcode = NormalizedBarcode(food), CreatedUtc = now, UpdatedUtc = now
        };
        entity.Nutrients.AddRange(food.Per100.Select(n => new FoodNutrient { NutrientId = n.Key, Amount = n.Value }));
        entity.Portions.AddRange(food.Portions.Select(p => new Portion { Name = p.Name.Trim(), Grams = p.Grams }));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Foods.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<EditableFood?> GetEditableAsync(int foodId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var food = await db.Foods.AsNoTracking().Include(f => f.Nutrients).Include(f => f.Portions).AsSplitQuery()
            .SingleOrDefaultAsync(f => f.Id == foodId, cancellationToken);
        if (food is null || !IsEditable(food.Source))
            return null;
        return new EditableFood(food.Id, food.Name, food.Basis, food.Source, food.Barcode,
            food.Nutrients.ToDictionary(n => n.NutrientId, n => n.Amount),
            food.Portions.OrderBy(p => p.Id).Select(p => new NewPortion(p.Name, p.Grams)).ToList());
    }

    public async Task<bool> UpdateAsync(int foodId, NewFood food, CancellationToken cancellationToken = default)
    {
        var name = Validate(food);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Foods.Include(f => f.Nutrients).SingleOrDefaultAsync(f => f.Id == foodId, cancellationToken);
        if (entity is null || !IsEditable(entity.Source))
            return false;
        entity.Name = name;
        entity.Basis = food.Basis;
        entity.UpdatedUtc = time.GetUtcNow().UtcDateTime;
        // Existing portions stay: diary entries refer to them.
        db.FoodNutrients.RemoveRange(entity.Nutrients);
        db.FoodNutrients.AddRange(food.Per100.Select(n => new FoodNutrient { FoodId = foodId, NutrientId = n.Key, Amount = n.Value }));
        db.Portions.AddRange(food.Portions.Select(p => new Portion { FoodId = foodId, Name = p.Name.Trim(), Grams = p.Grams }));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    static string Validate(NewFood food)
    {
        var name = food.Name.Trim();
        if (name.Length is 0 or > AppDbContext.MaxNameLength)
            throw new InvalidFoodException($"The name must be 1 to {AppDbContext.MaxNameLength} characters.");
        foreach (var (id, amount) in food.Per100)
        {
            if (!NutrientIds.Contains(id))
                throw new InvalidFoodException($"Unknown nutrient '{id}'.");
            if (!double.IsFinite(amount) || amount < 0)
                throw new InvalidFoodException($"The amount of {id} can't be negative.");
        }
        foreach (var portion in food.Portions)
        {
            if (portion.Name.Trim().Length is 0 or > AppDbContext.MaxPortionNameLength)
                throw new InvalidFoodException($"A portion's name must be 1 to {AppDbContext.MaxPortionNameLength} characters.");
            if (!double.IsFinite(portion.Grams) || portion.Grams <= 0)
                throw new InvalidFoodException("A portion must weigh more than 0.");
        }
        if (NormalizedBarcode(food) is { } code && !BarcodeLookup.IsValid(code))
            throw new InvalidFoodException("A barcode has 8, 12, 13 or 14 digits.");
        return name;
    }

    static string? NormalizedBarcode(NewFood food) => string.IsNullOrWhiteSpace(food.Barcode) ? null : food.Barcode.Trim();
}
