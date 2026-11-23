using System.Globalization;
using NutriLog.Data;

namespace NutriLog.Features.Diary;

public static class DiaryUrls
{
    public const string DateFormat = "yyyy-MM-dd";

    public static string Day(DateOnly date) => $"?date={Format(date)}";

    public static string Add(DateOnly date, int? foodId = null) =>
        $"add?date={Format(date)}" + (foodId is { } id ? $"&food={id}" : "");

    public static string AddAgain(DateOnly date, LoggedAmount logged)
    {
        var quantity = logged.Quantity.ToString("R", CultureInfo.InvariantCulture);
        if (logged.RecipeId is { } recipeId)
            return $"add?date={Format(date)}&recipe={recipeId}&{(logged.RecipeUnit == Recipes.RecipeUnit.Grams ? "grams" : "servings")}={quantity}";
        return $"add?date={Format(date)}&food={logged.FoodId}&amount={quantity}" + (logged.PortionId is { } portionId ? $"&portion={portionId}" : "");
    }

    public static string Entry(int entryId) => $"entry/{entryId}";

    public static string NewFood(DateOnly date) => $"foods/new?date={Format(date)}";

    public static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static string TimeOfDay(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DayTitle(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "Today",
        -1 => "Yesterday",
        1 => "Tomorrow",
        _ => date.ToString(date.Year == today.Year ? "ddd d MMM" : "ddd d MMM yyyy", CultureInfo.InvariantCulture)
    };
}
