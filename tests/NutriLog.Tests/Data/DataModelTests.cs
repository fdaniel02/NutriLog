using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using NutriLog.Data;

namespace NutriLog.Tests.Data;

// Against a migrated PostgreSQL, so the constraints tested are the ones the migrations create.
[Collection("Postgres")]
public class DataModelTests(PostgresFixture postgres) : IAsyncLifetime
{
    PooledDbContextFactory<AppDbContext> factory = null!;

    public async ValueTask InitializeAsync() => factory = await MigratedDatabase.CreateAsync(postgres);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    async Task<AppDbContext> Db() => await factory.CreateDbContextAsync(Ct);

    static Food Bread() => new()
    {
        Name = "Wholemeal bread",
        Source = FoodSource.Nevo,
        SourceId = "1234",
        Basis = FoodBasis.Grams,
        Nutrients =
        [
            new FoodNutrient { NutrientId = Nutrients.Energy, Amount = 235 },
            new FoodNutrient { NutrientId = Nutrients.Protein, Amount = 9.6 },
        ],
        Portions = [new Portion { Name = "slice", Grams = 35 }],
    };

    async Task<Food> AddAsync(Food food)
    {
        await using var db = await Db();
        db.Foods.Add(food);
        await db.SaveChangesAsync(Ct);
        return food;
    }

    async Task<Recipe> AddRecipeAsync(int foodId)
    {
        await using var db = await Db();
        var recipe = new Recipe { Name = "Sandwiches", Servings = 2, Ingredients = [new RecipeIngredient { FoodId = foodId, Grams = 140 }] };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(Ct);
        return recipe;
    }

    async Task<PostgresException> SaveFailsAsync(Action<AppDbContext> change)
    {
        await using var db = await Db();
        change(db);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        return Assert.IsType<PostgresException>(error.InnerException);
    }

    static DiaryEntry Entry(Action<DiaryEntry> set)
    {
        var entry = new DiaryEntry { Date = new DateOnly(2026, 11, 22), Meal = Meal.Lunch, Quantity = 1 };
        set(entry);
        return entry;
    }

    [Fact]
    public async Task The_nutrients_are_seeded_in_order()
    {
        await using var db = await Db();

        var nutrients = await db.Nutrients.OrderBy(n => n.SortOrder).ToListAsync(Ct);

        Assert.Equal(Nutrients.All.Select(n => (n.Id, n.Name, n.Unit, n.Group)), nutrients.Select(n => (n.Id, n.Name, n.Unit, n.Group)));
        Assert.Equal(Enumerable.Range(1, nutrients.Count), nutrients.Select(n => n.SortOrder));
        Assert.Equal(("energy", "kcal"), (nutrients[0].Id, nutrients[0].Unit));
    }

    [Fact]
    public async Task A_nutrient_without_a_value_stays_missing()
    {
        var bread = await AddAsync(Bread());

        await using var db = await Db();
        var amounts = await db.FoodNutrients.Where(n => n.FoodId == bread.Id).ToDictionaryAsync(n => n.NutrientId, n => n.Amount, Ct);

        Assert.Equal(new Dictionary<string, double> { [Nutrients.Energy] = 235, [Nutrients.Protein] = 9.6 }, amounts);
        Assert.False(amounts.ContainsKey(Nutrients.Fibre));
    }

    [Fact]
    public async Task Timestamps_come_back_as_utc()
    {
        var created = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Unspecified);
        var food = Bread();
        food.CreatedUtc = created;
        food.UpdatedUtc = created;
        var bread = await AddAsync(food);

        await using var db = await Db();
        var stored = await db.Foods.SingleAsync(f => f.Id == bread.Id, Ct);

        Assert.Equal((created, DateTimeKind.Utc), (stored.CreatedUtc, stored.CreatedUtc.Kind));
    }

    [Fact]
    public async Task Deleting_a_food_deletes_its_nutrients_and_portions()
    {
        var bread = await AddAsync(Bread());

        await using var db = await Db();
        await db.Foods.Where(f => f.Id == bread.Id).ExecuteDeleteAsync(Ct);

        Assert.False(await db.FoodNutrients.AnyAsync(n => n.FoodId == bread.Id, Ct));
        Assert.False(await db.Portions.AnyAsync(p => p.FoodId == bread.Id, Ct));
    }

    [Fact]
    public async Task A_food_in_the_diary_cannot_be_deleted()
    {
        var bread = await AddAsync(Bread());
        await using (var db = await Db())
        {
            db.DiaryEntries.Add(Entry(e => (e.FoodId, e.Quantity, e.QuantityUnit) = (bread.Id, 70, QuantityUnit.Grams)));
            await db.SaveChangesAsync(Ct);
        }

        var error = await SaveFailsAsync(db => db.Foods.Remove(db.Foods.Single(f => f.Id == bread.Id)));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
    }

    [Fact]
    public async Task A_food_in_a_recipe_cannot_be_deleted()
    {
        var bread = await AddAsync(Bread());
        await AddRecipeAsync(bread.Id);

        var error = await SaveFailsAsync(db => db.Foods.Remove(db.Foods.Single(f => f.Id == bread.Id)));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
    }

    [Fact]
    public async Task Deleting_a_recipe_deletes_its_ingredients()
    {
        var bread = await AddAsync(Bread());
        var recipe = await AddRecipeAsync(bread.Id);

        await using var db = await Db();
        await db.Recipes.Where(r => r.Id == recipe.Id).ExecuteDeleteAsync(Ct);

        Assert.False(await db.RecipeIngredients.AnyAsync(i => i.RecipeId == recipe.Id, Ct));
    }

    [Fact]
    public async Task Source_ids_are_unique_per_source_but_own_foods_have_none()
    {
        await AddAsync(Bread());
        var own = () => new Food { Name = "Grandma's soup", Source = FoodSource.Own };
        await AddAsync(own());
        await AddAsync(own());

        var error = await SaveFailsAsync(db => db.Foods.Add(Bread()));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [Fact]
    public async Task Valid_diary_entries_are_saved()
    {
        var bread = await AddAsync(Bread());
        var recipe = await AddRecipeAsync(bread.Id);
        var slice = bread.Portions[0];

        await using var db = await Db();
        db.DiaryEntries.AddRange(
            Entry(e => (e.FoodId, e.Quantity, e.QuantityUnit) = (bread.Id, 70, QuantityUnit.Grams)),
            Entry(e => (e.FoodId, e.Quantity, e.QuantityUnit, e.PortionId) = (bread.Id, 2, QuantityUnit.Portion, slice.Id)),
            Entry(e => (e.RecipeId, e.Quantity, e.QuantityUnit) = (recipe.Id, 0.5, QuantityUnit.Servings)));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(3, await db.DiaryEntries.CountAsync(Ct));
    }

    public static TheoryData<string, string> InvalidEntries => new()
    {
        { "neither", "CK_DiaryEntries_FoodOrRecipe" },
        { "both", "CK_DiaryEntries_FoodOrRecipe" },
        { "portion without id", "CK_DiaryEntries_Portion" },
        { "grams with portion id", "CK_DiaryEntries_Portion" },
        { "food in servings", "CK_DiaryEntries_Servings" },
        { "recipe in grams", "CK_DiaryEntries_Servings" },
        { "zero", "CK_DiaryEntries_Quantity" },
    };

    [Theory]
    [MemberData(nameof(InvalidEntries))]
    public async Task Invalid_diary_entries_are_rejected(string kind, string constraint)
    {
        var bread = await AddAsync(Bread());
        var recipe = await AddRecipeAsync(bread.Id);
        var slice = bread.Portions[0].Id;
        var entry = kind switch
        {
            "neither" => Entry(e => e.QuantityUnit = QuantityUnit.Grams),
            "both" => Entry(e => (e.FoodId, e.RecipeId, e.QuantityUnit) = (bread.Id, recipe.Id, QuantityUnit.Servings)),
            "portion without id" => Entry(e => (e.FoodId, e.QuantityUnit) = (bread.Id, QuantityUnit.Portion)),
            "grams with portion id" => Entry(e => (e.FoodId, e.QuantityUnit, e.PortionId) = (bread.Id, QuantityUnit.Grams, slice)),
            "food in servings" => Entry(e => (e.FoodId, e.QuantityUnit) = (bread.Id, QuantityUnit.Servings)),
            "recipe in grams" => Entry(e => (e.RecipeId, e.QuantityUnit) = (recipe.Id, QuantityUnit.Grams)),
            "zero" => Entry(e => (e.FoodId, e.QuantityUnit, e.Quantity) = (bread.Id, QuantityUnit.Grams, 0)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var error = await SaveFailsAsync(db => db.DiaryEntries.Add(entry));

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task A_diary_entry_cannot_use_another_foods_portion()
    {
        var bread = await AddAsync(Bread());
        var other = Bread();
        other.SourceId = "5678";
        var otherSlice = (await AddAsync(other)).Portions[0].Id;

        var error = await SaveFailsAsync(db => db.DiaryEntries.Add(
            Entry(e => (e.FoodId, e.QuantityUnit, e.PortionId) = (bread.Id, QuantityUnit.Portion, otherSlice))));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData("CK_FoodNutrients_Amount")]
    [InlineData("CK_Portions_Grams")]
    [InlineData("CK_Recipes_Servings")]
    [InlineData("CK_RecipeIngredients_Grams")]
    public async Task Amounts_cannot_be_negative_or_zero(string constraint)
    {
        var bread = await AddAsync(Bread());

        var error = await SaveFailsAsync(db =>
        {
            switch (constraint)
            {
                case "CK_FoodNutrients_Amount":
                    db.FoodNutrients.Add(new FoodNutrient { FoodId = bread.Id, NutrientId = Nutrients.Fat, Amount = -1 });
                    break;
                case "CK_Portions_Grams":
                    db.Portions.Add(new Portion { FoodId = bread.Id, Name = "nothing", Grams = 0 });
                    break;
                case "CK_Recipes_Servings":
                    db.Recipes.Add(new Recipe { Name = "Air", Servings = 0 });
                    break;
                default:
                    db.Recipes.Add(new Recipe { Name = "Toast", Servings = 1, Ingredients = [new RecipeIngredient { FoodId = bread.Id, Grams = 0 }] });
                    break;
            }
        });

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), (error.SqlState, error.ConstraintName));
    }
}
