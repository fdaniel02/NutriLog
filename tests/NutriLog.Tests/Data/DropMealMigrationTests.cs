using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NutriLog.Data;

namespace NutriLog.Tests.Data;

[Collection("Postgres")]
public class DropMealMigrationTests(PostgresFixture postgres)
{
    const string MigrationBefore = "20261123115623_TimeEaten";

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_entries_keep_everything_but_their_meal()
    {
        var factory = new PooledDbContextFactory<AppDbContext>(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString(PostgresFixture.NewDatabaseName())).Options);
        await using var db = await factory.CreateDbContextAsync(Ct);
        await db.GetService<IMigrator>().MigrateAsync(MigrationBefore, cancellationToken: Ct);
        var bread = new Food { Name = "Bread", Source = FoodSource.Own, Basis = FoodBasis.Grams };
        db.Foods.Add(bread);
        await db.SaveChangesAsync(Ct);
        var date = new DateOnly(2026, 11, 22);
        var logged = new DateTime(2026, 11, 22, 7, 30, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "DiaryEntries" ("Date", "Meal", "TimeEaten", "FoodId", "Quantity", "QuantityUnit", "CreatedUtc")
            VALUES ({date}, 'Dinner', {new TimeOnly(19, 15)}, {bread.Id}, 80, 'Grams', {logged}),
                   ({date}, 'Breakfast', {new TimeOnly(8, 30)}, {bread.Id}, 120, 'Grams', {logged})
            """, Ct);

        await db.Database.MigrateAsync(Ct);

        var entries = await db.DiaryEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal([(date, new TimeOnly(19, 15), bread.Id, 80.0, logged), (date, new TimeOnly(8, 30), bread.Id, 120.0, logged)],
            entries.Select(e => (e.Date, e.TimeEaten, e.FoodId!.Value, e.Quantity, e.CreatedUtc)));
        var mealColumns = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_name = 'DiaryEntries' AND column_name = 'Meal'
            """).SingleAsync(Ct);
        Assert.Equal(0, mealColumns);
    }
}
