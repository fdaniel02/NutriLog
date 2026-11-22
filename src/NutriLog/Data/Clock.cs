namespace NutriLog.Data;

// Calendar dates in the user's time zone, independent of the server's (UTC in Docker).
public class Clock(TimeZoneInfo timeZone, TimeProvider time)
{
    public DateOnly Today => ToLocalDate(time.GetUtcNow().UtcDateTime);

    public DateOnly ToLocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone));
}
