using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NutriLog.Data;
using NutriLog.Tests.Data;
using static NutriLog.Tests.Import.SampleFiles;

namespace NutriLog.Tests.Import;

// The app itself, started the way the cluster's Job or a person would: it migrates, imports and exits, and never
// starts the web server. A separate process, so its connection string can't leak into other tests.
[Collection("Postgres")]
public sealed class ImportProcessTests(PostgresFixture postgres) : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("nutrilog-import-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task The_app_imports_and_exits()
    {
        var connectionString = postgres.ConnectionString(PostgresFixture.NewDatabaseName());
        var file = Path.Combine(folder, "NEVO2099_v1.0.csv");
        await File.WriteAllBytesAsync(file,
            Nevo(new NevoRow("9001", "Test fruit raw", "Testvrucht rauw", "per 100g", new() { ["ENERCC"] = "50" })).ToArray(),
            TestContext.Current.CancellationToken);
        var start = new ProcessStartInfo("dotnet", [Path.Combine(AppContext.BaseDirectory, "NutriLog.dll"), "import", "nevo", file])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment = { ["ConnectionStrings__Default"] = connectionString, ["ASPNETCORE_ENVIRONMENT"] = "Production" },
        };

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("\"EventId\":11", await output);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        Assert.Equal("Test fruit raw", (await db.Foods.SingleAsync(TestContext.Current.CancellationToken)).Name);
    }
}
