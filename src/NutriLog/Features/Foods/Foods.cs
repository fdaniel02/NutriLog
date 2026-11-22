using Microsoft.EntityFrameworkCore;
using NutriLog.Data;

namespace NutriLog.Features.Foods;

public sealed record NewPortion(string Name, double Grams);

// Per100 holds only the nutrients whose value is known; a missing one stays unknown, not 0.
public sealed record NewFood(string Name, FoodBasis Basis, IReadOnlyDictionary<string, double> Per100, IReadOnlyList<NewPortion> Portions);

public sealed class InvalidFoodException(string message) : Exception(message);

public interface IFoods
{
    Task<int> CreateOwnAsync(NewFood food, CancellationToken cancellationToken = default);
}

public sealed class Foods(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time) : IFoods
{
    static readonly HashSet<string> NutrientIds = [.. Data.Nutrients.All.Select(n => n.Id)];

    public async Task<int> CreateOwnAsync(NewFood food, CancellationToken cancellationToken = default)
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

        var now = time.GetUtcNow().UtcDateTime;
        var entity = new Food { Name = name, Source = FoodSource.Own, Basis = food.Basis, CreatedUtc = now, UpdatedUtc = now };
        entity.Nutrients.AddRange(food.Per100.Select(n => new FoodNutrient { NutrientId = n.Key, Amount = n.Value }));
        entity.Portions.AddRange(food.Portions.Select(p => new Portion { Name = p.Name.Trim(), Grams = p.Grams }));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Foods.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}
