using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using NutriLog.Data;
using NutriLog.Import;
using NutriLog.Tests.Data;

namespace NutriLog.Tests.Import;

[Collection("Postgres")]
public sealed class FoodImporterTests(PostgresFixture postgres) : IAsyncLifetime
{
    static readonly DateTimeOffset FirstImport = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    readonly FakeTimeProvider time = new(FirstImport);
    PooledDbContextFactory<AppDbContext> factory = null!;
    FoodImporter importer = null!;

    public async ValueTask InitializeAsync()
    {
        factory = await MigratedDatabase.CreateAsync(postgres);
        importer = new FoodImporter(factory, time);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static ImportedFood Food(string id, string name = "Apple", double energy = 52, params ImportedPortion[] portions) =>
        new(id, name, name + " nl", FoodBasis.Grams, new Dictionary<string, double> { [Nutrients.Energy] = energy, [Nutrients.Fibre] = 2.4 }, portions);

    static ImportedDataset Nevo(params ImportedFood[] foods) => new(FoodSource.Nevo, "2099/1.0", foods);

    async Task<List<Food>> FoodsAsync(FoodSource source = FoodSource.Nevo)
    {
        await using var db = await factory.CreateDbContextAsync(Ct);
        return await db.Foods.Where(f => f.Source == source).Include(f => f.Nutrients).Include(f => f.Portions)
            .OrderBy(f => f.SourceId).ToListAsync(Ct);
    }

    async Task<int> FoodIdAsync(string sourceId) => (await FoodsAsync()).Single(f => f.SourceId == sourceId).Id;

    async Task AddAsync<T>(T entity) where T : class
    {
        await using var db = await factory.CreateDbContextAsync(Ct);
        db.Add(entity);
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task A_first_import_adds_every_food_with_its_nutrients_and_portions()
    {
        var result = await importer.ImportAsync(Nevo(Food("1", portions: new ImportedPortion("medium", 180)), Food("2", "Pear")), Ct);

        Assert.Equal(new ImportResult(2, 0, 0, 0, 0), result);
        var foods = await FoodsAsync();
        Assert.Equal([("1", "Apple", "Apple nl"), ("2", "Pear", "Pear nl")], foods.Select(f => (f.SourceId!, f.Name, f.NameNl!)));
        Assert.Equal(new Dictionary<string, double> { [Nutrients.Energy] = 52, [Nutrients.Fibre] = 2.4 },
            foods[0].Nutrients.ToDictionary(n => n.NutrientId, n => n.Amount));
        Assert.Equal(("medium", 180.0, true), foods[0].Portions.Select(p => (p.Name, p.Grams, p.Imported)).Single());
        Assert.All(foods, f => Assert.Equal((FirstImport.UtcDateTime, FirstImport.UtcDateTime), (f.CreatedUtc, f.UpdatedUtc)));
    }

    [Fact]
    public async Task Importing_the_same_release_again_changes_nothing()
    {
        var data = Nevo(Food("1", portions: new ImportedPortion("medium", 180)));
        await importer.ImportAsync(data, Ct);
        time.Advance(TimeSpan.FromDays(1));

        var result = await importer.ImportAsync(data, Ct);

        Assert.Equal(new ImportResult(0, 0, 1, 0, 0), result);
        Assert.Equal(FirstImport.UtcDateTime, Assert.Single(await FoodsAsync()).UpdatedUtc);
    }

    [Fact]
    public async Task A_new_release_updates_foods_in_place()
    {
        await importer.ImportAsync(Nevo(Food("1", portions: [new ImportedPortion("medium", 180), new ImportedPortion("small", 120)])), Ct);
        var id = await FoodIdAsync("1");
        time.Advance(TimeSpan.FromDays(1));
        var renamed = Food("1", "Apple, raw", energy: 54, new ImportedPortion("medium", 175), new ImportedPortion("large", 220)) with
        {
            Nutrients = new Dictionary<string, double> { [Nutrients.Energy] = 54, [Nutrients.Protein] = 0.3 },
        };

        var result = await importer.ImportAsync(Nevo(renamed), Ct);

        Assert.Equal(new ImportResult(0, 1, 0, 0, 0), result);
        var food = Assert.Single(await FoodsAsync());
        Assert.Equal((id, "Apple, raw", FirstImport.UtcDateTime, time.GetUtcNow().UtcDateTime), (food.Id, food.Name, food.CreatedUtc, food.UpdatedUtc));
        Assert.Equal(new Dictionary<string, double> { [Nutrients.Energy] = 54, [Nutrients.Protein] = 0.3 },
            food.Nutrients.ToDictionary(n => n.NutrientId, n => n.Amount));
        Assert.Equal([("large", 220.0), ("medium", 175.0)], food.Portions.Select(p => (p.Name, p.Grams)).Order());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("basis")]
    [InlineData("portion")]
    public async Task Any_difference_counts_as_an_update(string change)
    {
        await importer.ImportAsync(Nevo(Food("1", portions: new ImportedPortion("medium", 180))), Ct);
        var food = Food("1", portions: new ImportedPortion("medium", 180));
        food = change switch
        {
            "name" => food with { NameNl = "Appel" },
            "basis" => food with { Basis = FoodBasis.Millilitres },
            _ => food with { Portions = [new ImportedPortion("medium", 190)] },
        };

        var result = await importer.ImportAsync(Nevo(food), Ct);

        Assert.Equal(1, result.Updated);
    }

    [Fact]
    public async Task Portions_added_by_hand_stay()
    {
        await importer.ImportAsync(Nevo(Food("1", portions: new ImportedPortion("medium", 180))), Ct);
        await AddAsync(new Portion { FoodId = await FoodIdAsync("1"), Name = "my apple", Grams = 150 });

        await importer.ImportAsync(Nevo(Food("1")), Ct);

        Assert.Equal(("my apple", false), (await FoodsAsync()).Single().Portions.Select(p => (p.Name, p.Imported)).Single());
    }

    [Fact]
    public async Task An_imported_portion_the_diary_uses_stays()
    {
        await importer.ImportAsync(Nevo(Food("1", portions: new ImportedPortion("medium", 180))), Ct);
        var food = (await FoodsAsync()).Single();
        await AddAsync(new DiaryEntry
        {
            Date = new DateOnly(2026, 11, 22), Meal = Meal.Snack, FoodId = food.Id, Quantity = 1,
            QuantityUnit = QuantityUnit.Portion, PortionId = food.Portions[0].Id,
        });

        await importer.ImportAsync(Nevo(Food("1")), Ct);

        Assert.Equal("medium", (await FoodsAsync()).Single().Portions.Single().Name);
    }

    [Fact]
    public async Task Foods_gone_from_the_release_are_removed_unless_in_use()
    {
        await importer.ImportAsync(Nevo(Food("1"), Food("2", "Pear"), Food("3", "Plum"), Food("4", "Kiwi")), Ct);
        await AddAsync(new DiaryEntry
        {
            Date = new DateOnly(2026, 11, 22), Meal = Meal.Snack, FoodId = await FoodIdAsync("2"), Quantity = 100, QuantityUnit = QuantityUnit.Grams,
        });
        await AddAsync(new Recipe { Name = "Jam", Servings = 10, Ingredients = [new RecipeIngredient { FoodId = await FoodIdAsync("3"), Grams = 500 }] });

        var result = await importer.ImportAsync(Nevo(Food("1")), Ct);

        Assert.Equal(new ImportResult(0, 0, 1, 1, 2), result);
        Assert.Equal(["1", "2", "3"], (await FoodsAsync()).Select(f => f.SourceId));
    }

    [Fact]
    public async Task Other_sources_are_left_alone()
    {
        await importer.ImportAsync(new ImportedDataset(FoodSource.UsdaSrLegacy, "2018-04", [Food("1")]), Ct);
        await AddAsync(new Food { Name = "Grandma's soup", Source = FoodSource.Own });

        await importer.ImportAsync(new ImportedDataset(FoodSource.UsdaFoundation, "2099-04-30", [Food("2")]), Ct);
        await importer.ImportAsync(Nevo(), Ct);

        Assert.Single(await FoodsAsync(FoodSource.UsdaSrLegacy));
        Assert.Single(await FoodsAsync(FoodSource.UsdaFoundation));
        Assert.Single(await FoodsAsync(FoodSource.Own));
    }

    [Fact]
    public async Task Each_import_is_recorded()
    {
        await importer.ImportAsync(Nevo(Food("1"), Food("2", "Pear")), Ct);
        time.Advance(TimeSpan.FromDays(1));
        await importer.ImportAsync(Nevo(Food("1")), Ct);

        await using var db = await factory.CreateDbContextAsync(Ct);
        var imports = await db.DataImports.OrderBy(i => i.Id).ToListAsync(Ct);
        Assert.Equal(
            [(FoodSource.Nevo, "2099/1.0", FirstImport.UtcDateTime, 2, 0, 0, 0, 0), (FoodSource.Nevo, "2099/1.0", time.GetUtcNow().UtcDateTime, 0, 0, 1, 1, 0)],
            imports.Select(i => (i.Source, i.Version, i.ImportedUtc, i.Added, i.Updated, i.Unchanged, i.Removed, i.Kept)));
    }

    [Fact]
    public async Task A_source_id_twice_is_refused_before_anything_changes()
    {
        var error = await Assert.ThrowsAsync<ImportFormatException>(() => importer.ImportAsync(Nevo(Food("1"), Food("1", "Pear")), Ct));

        Assert.Contains("1", error.Message);
        Assert.Empty(await FoodsAsync());
    }
}
