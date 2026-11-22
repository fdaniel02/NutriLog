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

    static readonly Dictionary<string, NewFood> Invalid = new()
    {
        ["blank name"] = new NewFood(" ", FoodBasis.Grams, new Dictionary<string, double>(), []),
        ["long name"] = new NewFood(new string('x', AppDbContext.MaxNameLength + 1), FoodBasis.Grams, new Dictionary<string, double>(), []),
        ["unknown nutrient"] = new NewFood("Bar", FoodBasis.Grams, new Dictionary<string, double> { ["unobtainium"] = 1 }, []),
        ["negative amount"] = new NewFood("Bar", FoodBasis.Grams, new Dictionary<string, double> { [Nutrients.Fat] = -1 }, []),
        ["unnamed portion"] = new NewFood("Bar", FoodBasis.Grams, new Dictionary<string, double>(), [new NewPortion("", 10)]),
        ["empty portion"] = new NewFood("Bar", FoodBasis.Grams, new Dictionary<string, double>(), [new NewPortion("bar", 0)]),
    };

    public static TheoryData<string> InvalidCases => [.. Invalid.Keys];

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task Rejects_invalid_foods(string invalidCase)
    {
        var food = Invalid[invalidCase];
        var (foods, factory) = await CreateAsync(postgres);

        await Assert.ThrowsAsync<InvalidFoodException>(() => foods.CreateOwnAsync(food, Token));

        await using var db = await factory.CreateDbContextAsync(Token);
        Assert.False(await db.Foods.AnyAsync(Token));
    }

    [Fact]
    public async Task Saves_the_barcode_of_an_own_food()
    {
        var (foods, factory) = await CreateAsync(postgres);

        var id = await foods.CreateOwnAsync(new NewFood("Crackers", FoodBasis.Grams, new Dictionary<string, double>(), [], " 87123456 "), Token);

        await using var db = await factory.CreateDbContextAsync(Token);
        Assert.Equal("87123456", (await db.Foods.SingleAsync(f => f.Id == id, Token)).Barcode);
    }

    [Fact]
    public async Task Rejects_an_invalid_barcode() =>
        await Assert.ThrowsAsync<InvalidFoodException>(async () =>
            await (await CreateAsync(postgres)).Foods.CreateOwnAsync(new NewFood("Crackers", FoodBasis.Grams, new Dictionary<string, double>(), [], "123"), Token));

    static async Task<int> AddFoodAsync(IDbContextFactory<AppDbContext> factory, FoodSource source)
    {
        await using var db = await factory.CreateDbContextAsync(Token);
        var food = new Food { Name = "Crackers", Source = source, SourceId = source == FoodSource.Own ? null : "123", Barcode = "87123456" };
        food.Nutrients.Add(new FoodNutrient { NutrientId = Nutrients.Energy, Amount = 400 });
        food.Nutrients.Add(new FoodNutrient { NutrientId = Nutrients.Fat, Amount = 10 });
        food.Portions.Add(new Portion { Name = "serving", Grams = 25, Imported = true });
        db.Foods.Add(food);
        await db.SaveChangesAsync(Token);
        return food.Id;
    }

    [Fact]
    public async Task Returns_an_open_food_facts_food_for_editing()
    {
        var (foods, factory) = await CreateAsync(postgres);
        var id = await AddFoodAsync(factory, FoodSource.Off);

        var food = await foods.GetEditableAsync(id, Token);

        Assert.Equal((id, "Crackers", FoodSource.Off, "87123456"), (food!.Id, food.Name, food.Source, food.Barcode));
        Assert.Equal(400, food.Per100[Nutrients.Energy]);
        Assert.Equal([new NewPortion("serving", 25)], food.Portions);
    }

    [Fact]
    public async Task Replaces_the_values_and_keeps_the_portions()
    {
        var (foods, factory) = await CreateAsync(postgres);
        var id = await AddFoodAsync(factory, FoodSource.Off);

        var updated = await foods.UpdateAsync(id, new NewFood(" Oat crackers ", FoodBasis.Grams,
            new Dictionary<string, double> { [Nutrients.Energy] = 420, [Nutrients.Protein] = 11 }, [new NewPortion("3 crackers", 20)]), Token);

        Assert.True(updated);
        await using var db = await factory.CreateDbContextAsync(Token);
        var food = await db.Foods.Include(f => f.Nutrients).Include(f => f.Portions).SingleAsync(f => f.Id == id, Token);
        Assert.Equal(("Oat crackers", new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc)), (food.Name, food.UpdatedUtc));
        Assert.Equal([(Nutrients.Energy, 420.0), (Nutrients.Protein, 11.0)], food.Nutrients.OrderBy(n => n.NutrientId).Select(n => (n.NutrientId, n.Amount)));
        Assert.Equal(["serving", "3 crackers"], food.Portions.OrderBy(p => p.Id).Select(p => p.Name));
    }

    [Fact]
    public async Task An_imported_food_cant_be_edited()
    {
        var (foods, factory) = await CreateAsync(postgres);
        var id = await AddFoodAsync(factory, FoodSource.Nevo);

        Assert.Null(await foods.GetEditableAsync(id, Token));
        Assert.False(await foods.UpdateAsync(id, new NewFood("Other", FoodBasis.Grams, new Dictionary<string, double>(), []), Token));
        Assert.False(await foods.UpdateAsync(id + 1, new NewFood("Other", FoodBasis.Grams, new Dictionary<string, double>(), []), Token));
        await using var db = await factory.CreateDbContextAsync(Token);
        Assert.Equal("Crackers", (await db.Foods.SingleAsync(f => f.Id == id, Token)).Name);
    }
}
