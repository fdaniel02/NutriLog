using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NutriLog.Data;

namespace NutriLog.Tests.Data;

[Collection("Postgres")]
public class RecipeGramsMigrationTests(PostgresFixture postgres)
{
    const string MigrationBefore = "20261123150905_DropMeal";

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_recipe_entries_get_their_recipes_weight_per_serving_and_back()
    {
        var factory = new PooledDbContextFactory<AppDbContext>(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString(PostgresFixture.NewDatabaseName())).Options);
        await using var db = await factory.CreateDbContextAsync(Ct);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore, cancellationToken: Ct);
        var bread = new Food { Name = "Bread", Source = FoodSource.Own, Basis = FoodBasis.Grams };
        var milk = new Food { Name = "Milk", Source = FoodSource.Own, Basis = FoodBasis.Millilitres };
        db.Foods.AddRange(bread, milk);
        await db.SaveChangesAsync(Ct);
        var date = new DateOnly(2026, 11, 22);
        var logged = new DateTime(2026, 11, 22, 7, 30, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Recipes" ("Name", "Servings", "CreatedUtc", "UpdatedUtc") VALUES ('Toast', 4, {logged}, {logged});
            INSERT INTO "RecipeIngredients" ("RecipeId", "FoodId", "Grams")
            SELECT "Id", {bread.Id}, 300 FROM "Recipes" UNION ALL SELECT "Id", {milk.Id}, 100 FROM "Recipes";
            INSERT INTO "DiaryEntries" ("Date", "TimeEaten", "RecipeId", "Quantity", "QuantityUnit", "CreatedUtc")
            SELECT {date}, {new TimeOnly(8, 0)}, "Id", 1.5, 'Servings', {logged} FROM "Recipes";
            INSERT INTO "DiaryEntries" ("Date", "TimeEaten", "FoodId", "Quantity", "QuantityUnit", "CreatedUtc")
            VALUES ({date}, {new TimeOnly(9, 0)}, {bread.Id}, 80, 'Grams', {logged});
            """, Ct);

        await db.Database.MigrateAsync(Ct);

        var entries = await db.DiaryEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal([(1.5, QuantityUnit.Servings, (double?)100), (80, QuantityUnit.Grams, null)],
            entries.Select(e => (e.Quantity, e.QuantityUnit, e.ServingGrams)));

        await db.Database.ExecuteSqlAsync($"""UPDATE "DiaryEntries" SET "Quantity" = 250, "QuantityUnit" = 'Grams' WHERE "RecipeId" IS NOT NULL""", Ct);
        await migrator.MigrateAsync(MigrationBefore, cancellationToken: Ct);

        var down = await db.Database.SqlQuery<double>($"""
            SELECT "Quantity" AS "Value" FROM "DiaryEntries" WHERE "RecipeId" IS NOT NULL AND "QuantityUnit" = 'Servings'
            """).SingleAsync(Ct);
        Assert.Equal(2.5, down);
    }
}
