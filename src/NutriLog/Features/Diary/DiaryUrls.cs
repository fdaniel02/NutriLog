using System.Globalization;
using NutriLog.Data;

namespace NutriLog.Features.Diary;

public static class DiaryUrls
{
    public const string DateFormat = "yyyy-MM-dd";

    public static string Day(DateOnly date) => $"?date={Format(date)}";

    public static string Add(DateOnly date, Meal meal, int? foodId = null) =>
        $"add?date={Format(date)}&meal={meal}" + (foodId is { } id ? $"&food={id}" : "");

    public static string Entry(int entryId) => $"entry/{entryId}";

    public static string NewFood(DateOnly date, Meal meal) => $"foods/new?date={Format(date)}&meal={meal}";

    public static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static Meal? ParseMeal(string? text) =>
        Enum.TryParse<Meal>(text, ignoreCase: true, out var meal) && Enum.IsDefined(meal) ? meal : null;

    public static string TimeOfDay(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DayTitle(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "Today",
        -1 => "Yesterday",
        1 => "Tomorrow",
        _ => date.ToString(date.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy", CultureInfo.InvariantCulture)
    };
}
