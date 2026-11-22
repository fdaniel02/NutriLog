using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NutriLog.Data;
using NutriLog.Features.Foods;
using NutriLog.Tests.Data;

namespace NutriLog.Tests.Features.Foods;

[Collection("Postgres")]
public class FoodsTests(PostgresFixture postgres)
{
    static CancellationToken Token => TestContext.Current.CancellationToken;

    static async Task<(NutriLog.Features.Foods.Foods Foods, IDbContextFactory<AppDbContext> Factory)> CreateAsync(PostgresFixture postgres)
    {
        var factory = await MigratedDatabase.CreateAsync(postgres);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero));
        return (new NutriLog.Features.Foods.Foods(factory, time), factory);
    }

    [Fact]
    public async Task Saves_an_own_food_with_its_values_and_portions()
    {
        var (foods, factory) = await CreateAsync(postgres);

        var id = await foods.CreateOwnAsync(new NewFood(" Protein bar ", FoodBasis.Grams,
            new Dictionary<string, double> { [Nutrients.Energy] = 380, [Nutrients.Protein] = 30 },
            [new NewPortion(" bar ", 55)]), Token);

        await using var db = await factory.CreateDbContextAsync(Token);
        var food = await db.Foods.Include(f => f.Nutrients).Include(f => f.Portions).SingleAsync(f => f.Id == id, Token);
        Assert.Equal(("Protein bar", FoodSource.Own, (string?)null), (food.Name, food.Source, food.SourceId));
        Assert.Equal(new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc), food.CreatedUtc);
        Assert.Equal([(Nutrients.Energy, 380.0), (Nutrients.Protein, 30.0)], food.Nutrients.OrderBy(n => n.NutrientId).Select(n => (n.NutrientId, n.Amount)));
        Assert.Equal(("bar", 55.0, false), (food.Portions[0].Name, food.Portions[0].Grams, food.Portions[0].Imported));
    }

    public static TheoryData<NewFood> Invalid => new()
    {
        new NewFood(" ", FoodBasis.Grams, new Dictionary<string, double>(), []),
        new NewFood(new string('x', AppDbContext.MaxNameLength + 1), FoodBasis.Grams, new Dictionary<string, double>(), []),
        new NewFood("Bar", FoodBasis.Grams, new Dictionary<string, double> { ["unobtainium"] = 1 }, []),
        new NewFood("Bar", FoodBasis.Grams, new Dictionary<string, double> { [Nutrients.Fat] = -1 }, []),
        new NewFood("Bar", FoodBasis.Grams, new Dictionary<string, double>(), [new NewPortion("", 10)]),
        new NewFood("Bar", FoodBasis.Grams, new Dictionary<string, double>(), [new NewPortion("bar", 0)]),
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Rejects_invalid_foods(NewFood food)
    {
        var (foods, factory) = await CreateAsync(postgres);

        await Assert.ThrowsAsync<InvalidFoodException>(() => foods.CreateOwnAsync(food, Token));

        await using var db = await factory.CreateDbContextAsync(Token);
        Assert.False(await db.Foods.AnyAsync(Token));
    }
}
