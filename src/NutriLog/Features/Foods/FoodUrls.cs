using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Features.Foods;

public static class FoodUrls
{
    public static string Edit(int foodId, DateOnly date, Meal meal) =>
        $"foods/{foodId}/edit?date={DiaryUrls.Format(date)}&meal={meal}";

    public static string NewWithBarcode(DateOnly date, Meal meal, string barcode) =>
        $"{DiaryUrls.NewFood(date, meal)}&barcode={Uri.EscapeDataString(barcode)}";
}
