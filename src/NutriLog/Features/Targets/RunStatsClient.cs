using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace NutriLog.Features.Targets;

public interface IRunStats
{
    // Null when RunStats can't be reached or answers with something unexpected.
    Task<RunDay?> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default);
}

public sealed class RunStatsClient(HttpClient http, ILogger<RunStatsClient> logger) : IRunStats
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<RunDay?> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try
        {
            var runs = await http.GetFromJsonAsync<List<RunRow>>($"api/runs?from={day}&to={day}", cancellationToken) ?? [];
            return new RunDay(runs.Count, runs.Sum(r => r.DistanceMeters) / 1000, runs.Sum(r => r.Calories ?? 0),
                runs.Count(r => r.Calories is null));
        }
        // HttpClient's timeout surfaces as a TaskCanceledException too; only the caller's cancellation is passed on.
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.RunStatsUnavailable(day, ex.GetType().Name, ex.Message);
            return null;
        }
    }

    // Without the trailing slash, the relative request path would replace the base address' last segment.
    public static Uri BaseAddress(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? throw new InvalidOperationException("Setting 'RunStats:BaseUrl' is missing (e.g. 'http://runstats.runstats.svc').")
            : new Uri(configured.TrimEnd('/') + "/");

    // Only what the targets use of RunStats' GET /api/runs rows.
    sealed record RunRow(double DistanceMeters, double? Calories);
}
