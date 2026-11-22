using System.Diagnostics;

namespace NutriLog.Import;

// `NutriLog import nevo <file>` or `NutriLog import usda <zip>`: runs instead of the web app, after the migrations.
public sealed class ImportCommand(FoodImporter importer, TimeProvider time, ILogger<ImportCommand> logger)
{
    public const int Succeeded = 0;
    public const int Failed = 1;
    public const int Usage = 2;

    public const string UsageText =
        "Usage: NutriLog import nevo <NEVO<year>_v<version>.csv> | NutriLog import usda <FoodData_Central_..._json_<date>.zip>";

    public static bool IsImport(string[] args) => args is ["import", ..];

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args is not ["import", var source, var path] || source is not ("nevo" or "usda"))
        {
            await Console.Error.WriteLineAsync(UsageText);
            return Usage;
        }

        logger.ImportStarted(path);
        var started = time.GetTimestamp();
        try
        {
            await using var file = File.OpenRead(path);
            var data = source == "nevo" ? NevoReader.Read(file) : UsdaReader.Read(file, Path.GetFileName(path));
            var result = await importer.ImportAsync(data, cancellationToken);
            var seconds = time.GetElapsedTime(started).TotalSeconds;
            logger.ImportSucceeded(data.Source, data.Version, result.Added, result.Updated, result.Unchanged, result.Removed,
                result.Kept, seconds);
            return Succeeded;
        }
        // Problems with the file itself: a message is enough, a stack trace wouldn't help.
        catch (Exception ex) when (ex is ImportFormatException or IOException or UnauthorizedAccessException
                                       or System.Text.Json.JsonException or InvalidDataException)
        {
            logger.ImportFailed(path, ex.Message);
            return Failed;
        }
    }
}
