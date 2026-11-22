using NutriLog.Data;
using NutriLog.Features.Sources;
using NutriLog.Tests.Data;

namespace NutriLog.Tests.Features;

[Collection("Postgres")]
public class SourceQueriesTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Returns_the_latest_version_per_source()
    {
        var factory = await MigratedDatabase.CreateAsync(postgres);
        await using (var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var day = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);
            db.DataImports.AddRange(
                new DataImport { Source = FoodSource.Nevo, Version = "2025/9.0", ImportedUtc = day },
                new DataImport { Source = FoodSource.Nevo, Version = "2027/10.0", ImportedUtc = day.AddDays(400) },
                new DataImport { Source = FoodSource.UsdaSrLegacy, Version = "2018-04", ImportedUtc = day });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var versions = await new SourceQueries(factory).ImportedVersionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Dictionary<FoodSource, string> { [FoodSource.Nevo] = "2027/10.0", [FoodSource.UsdaSrLegacy] = "2018-04" }, versions);
    }
}
