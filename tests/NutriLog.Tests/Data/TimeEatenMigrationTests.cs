using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NutriLog.Data;

namespace NutriLog.Tests.Data;

[Collection("Postgres")]
public class TimeEatenMigrationTests(PostgresFixture postgres)
{
    const string MigrationBefore = "20261122194036_Targets";

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_entries_count_as_eaten_when_they_were_logged_in_amsterdam_time()
    {
        var factory = new PooledDbContextFactory<AppDbContext>(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.ConnectionString(PostgresFixture.NewDatabaseName())).Options);
        await using var db = await factory.CreateDbContextAsync(Ct);
        await db.GetService<IMigrator>().MigrateAsync(MigrationBefore, cancellationToken: Ct);
        var bread = new Food { Name = "Bread", Source = FoodSource.Own, Basis = FoodBasis.Grams };
        db.Foods.Add(bread);
        await db.SaveChangesAsync(Ct);
        DateTime[] logged =
        [
            new(2026, 11, 22, 7, 30, 45, DateTimeKind.Utc),
            new(2026, 7, 1, 6, 15, 10, DateTimeKind.Utc),
            new(2026, 11, 22, 23, 10, 0, DateTimeKind.Utc)
        ];
        foreach (var createdUtc in logged)
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "DiaryEntries" ("Date", "Meal", "FoodId", "Quantity", "QuantityUnit", "CreatedUtc")
                VALUES ({new DateOnly(2026, 11, 22)}, 'Lunch', {bread.Id}, 100, 'Grams', {createdUtc})
                """, Ct);

        await db.Database.MigrateAsync(Ct);

        var times = await db.DiaryEntries.OrderBy(e => e.Id).Select(e => e.TimeEaten).ToListAsync(Ct);
        // Winter time is UTC+1, summer time UTC+2.
        Assert.Equal([new TimeOnly(8, 30), new TimeOnly(8, 15), new TimeOnly(0, 10)], times);
    }
}
