using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Features.Foods;

public static class FoodUrls
{
    public static string Edit(int foodId, DateOnly date) =>
        $"foods/{foodId}/edit?date={DiaryUrls.Format(date)}";

    public static string NewWithBarcode(DateOnly date, string barcode) =>
        $"{DiaryUrls.NewFood(date)}&barcode={Uri.EscapeDataString(barcode)}";
}
