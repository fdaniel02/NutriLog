using NutriLog.Data;

namespace NutriLog;

// Event ids are what log queries and alerts in Grafana filter on; keep them stable when messages change.
public static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "{Operation} attempt {Attempt}/{MaxAttempts} failed, retrying: {ErrorType}: {ErrorMessage}")]
    public static partial void StartupAttemptFailed(
        this ILogger logger, string operation, int attempt, int maxAttempts, string errorType, string errorMessage);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{Operation} succeeded after {Attempts} attempts")]
    public static partial void StartupSucceededAfterRetries(this ILogger logger, string operation, int attempts);

    [LoggerMessage(EventId = 3, Level = LogLevel.Critical, Message = "Application stopped because of an unhandled error")]
    public static partial void ApplicationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Import of {File} started")]
    public static partial void ImportStarted(this ILogger logger, string file);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information,
        Message = "Import of {Source} {Version}: {Added} foods added, {Updated} updated, {Unchanged} unchanged, {Removed} removed, {Kept} kept because they're in use, in {Seconds:0.0} s")]
    public static partial void ImportSucceeded(
        this ILogger logger, FoodSource source, string version, int added, int updated, int unchanged, int removed, int kept, double seconds);

    [LoggerMessage(EventId = 12, Level = LogLevel.Error, Message = "Import of {File} failed: {Reason}")]
    public static partial void ImportFailed(this ILogger logger, string file, string reason);

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning,
        Message = "RunStats couldn't be reached for the runs of {Date}, the day shows the base targets: {ErrorType}: {ErrorMessage}")]
    public static partial void RunStatsUnavailable(this ILogger logger, string date, string errorType, string errorMessage);
}
