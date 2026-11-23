namespace NutriLog.Data;

// Calendar dates in the user's time zone, independent of the server's (UTC in Docker).
public class Clock(TimeZoneInfo timeZone, TimeProvider time)
{
    public DateOnly Today => ToLocalDate(time.GetUtcNow().UtcDateTime);

    // Whole minutes, as the time field shows them.
    public TimeOnly Now
    {
        get
        {
            var now = TimeOnly.FromDateTime(ToLocal(time.GetUtcNow().UtcDateTime));
            return new TimeOnly(now.Hour, now.Minute);
        }
    }

    public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
}
