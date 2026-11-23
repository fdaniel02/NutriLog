using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Tests.Features.Diary;

// A migrated database with a few foods, and the diary on top of it.
public sealed class DiaryData
{
    public static readonly DateOnly Today = new(2026, 11, 22);
    public static readonly TimeOnly Noon = new(12, 0);

    public required IDbContextFactory<AppDbContext> Factory { get; init; }
    public required FakeTimeProvider Time { get; init; }
    public required Clock Clock { get; init; }
    public required NutriLog.Features.Diary.Diary Diary { get; init; }

    public static async Task<DiaryData> CreateAsync(Data.PostgresFixture postgres)
    {
        var factory = await Data.MigratedDatabase.CreateAsync(postgres);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero));
        var clock = new Clock(TimeZoneInfo.Utc, time);
        return new DiaryData { Factory = factory, Time = time, Clock = clock, Diary = new NutriLog.Features.Diary.Diary(factory, clock, time) };
    }

    public async Task<Food> AddFoodAsync(string name, Dictionary<string, double> per100, params (string Name, double Grams)[] portions)
    {
        await using var db = await Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var food = new Food { Name = name, Source = FoodSource.Nevo, SourceId = Guid.NewGuid().ToString("N")[..10], Basis = FoodBasis.Grams };
        food.Nutrients.AddRange(per100.Select(n => new FoodNutrient { NutrientId = n.Key, Amount = n.Value }));
        food.Portions.AddRange(portions.Select(p => new Portion { Name = p.Name, Grams = p.Grams, Imported = true }));
        db.Foods.Add(food);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return food;
    }

    public static Dictionary<string, double> Macros(double energy, double protein, double carbs, double fat) => new()
    {
        [Nutrients.Energy] = energy,
        [Nutrients.Protein] = protein,
        [Nutrients.Carbohydrate] = carbs,
        [Nutrients.Fat] = fat
    };
}
