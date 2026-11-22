using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NutriLog.Data;
using NutriLog.Import;
using NutriLog.Tests.Data;
using static NutriLog.Tests.Import.SampleFiles;

namespace NutriLog.Tests.Import;

[Collection("Postgres")]
public sealed class ImportCommandTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    readonly FakeLogger<ImportCommand> logger = new();
    readonly string folder = Directory.CreateTempSubdirectory("nutrilog-import-").FullName;
    PooledDbContextFactory<AppDbContext> factory = null!;
    ImportCommand command = null!;

    public async ValueTask InitializeAsync()
    {
        factory = await MigratedDatabase.CreateAsync(postgres);
        command = new ImportCommand(new FoodImporter(factory, TimeProvider.System), TimeProvider.System, logger);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    string Save(string name, MemoryStream content)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, content.ToArray());
        return path;
    }

    async Task<List<(FoodSource, string)>> FoodsAsync()
    {
        await using var db = await factory.CreateDbContextAsync(Ct);
        return (await db.Foods.OrderBy(f => f.Source).ThenBy(f => f.SourceId).ToListAsync(Ct)).Select(f => (f.Source, f.SourceId!)).ToList();
    }

    [Fact]
    public async Task Imports_a_nevo_file_and_logs_the_counts()
    {
        var file = Save("NEVO2099_v1.0.csv", Nevo(new NevoRow("9001", "Test fruit raw", "Testvrucht rauw", "per 100g", new() { ["ENERCC"] = "50" })));

        var exitCode = await command.RunAsync(["import", "nevo", file], Ct);

        Assert.Equal(ImportCommand.Succeeded, exitCode);
        Assert.Equal([(FoodSource.Nevo, "9001")], await FoodsAsync());
        var records = logger.Collector.GetSnapshot();
        Assert.Equal([10, 11], records.Select(r => r.Id.Id));
        Assert.Equal(("2099/1.0", "1", "0"), (records[1].GetStructuredStateValue("Version"), records[1].GetStructuredStateValue("Added"), records[1].GetStructuredStateValue("Removed")));
    }

    [Fact]
    public async Task Imports_a_usda_zip()
    {
        var file = Save("FoodData_Central_sr_legacy_food_json_2018-04.zip",
            UsdaZip("SRLegacyFoods", new UsdaFood(167512, "Test biscuits", new() { [1008] = 300 })));

        var exitCode = await command.RunAsync(["import", "usda", file], Ct);

        Assert.Equal(ImportCommand.Succeeded, exitCode);
        Assert.Equal([(FoodSource.UsdaSrLegacy, "167512")], await FoodsAsync());
    }

    [Theory]
    [InlineData("import")]
    [InlineData("import nevo")]
    [InlineData("import off file.json")]
    [InlineData("import nevo a.csv b.csv")]
    public async Task Wrong_arguments_print_the_usage(string commandLine)
    {
        var args = commandLine.Split(' ');
        var error = new StringWriter();
        var previous = Console.Error;
        Console.SetError(error);
        try
        {
            Assert.Equal(ImportCommand.Usage, await command.RunAsync(args, Ct));
        }
        finally
        {
            Console.SetError(previous);
        }

        Assert.Contains("Usage: NutriLog import nevo", error.ToString());
        Assert.Empty(logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task A_missing_file_fails_with_a_message()
    {
        var file = Path.Combine(folder, "missing.csv");

        var exitCode = await command.RunAsync(["import", "nevo", file], Ct);

        Assert.Equal(ImportCommand.Failed, exitCode);
        var failure = logger.Collector.LatestRecord;
        Assert.Equal((12, LogLevel.Error, file), (failure.Id.Id, failure.Level, failure.GetStructuredStateValue("File")));
        Assert.Null(failure.Exception);
    }

    [Theory]
    [InlineData("nevo", "not a nevo file")]
    [InlineData("usda", "not a zip")]
    public async Task A_file_of_the_wrong_kind_fails_with_a_message(string source, string content)
    {
        var file = Save("FoodData_Central_sr_legacy_food_json_2018-04.zip", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)));

        var exitCode = await command.RunAsync(["import", source, file], Ct);

        Assert.Equal(ImportCommand.Failed, exitCode);
        Assert.Equal(12, logger.Collector.LatestRecord.Id.Id);
        Assert.Empty(await FoodsAsync());
    }

    [Theory]
    [InlineData("import nevo x", true)]
    [InlineData("import", true)]
    [InlineData("", false)]
    [InlineData("--urls http://localhost:5000", false)]
    public void Recognises_an_import(string commandLine, bool expected) =>
        Assert.Equal(expected, ImportCommand.IsImport(commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
}
