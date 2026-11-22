using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Features.Barcode;

public static class BarcodeUrls
{
    public static string Scan(DateOnly date, Meal meal) => $"scan?date={DiaryUrls.Format(date)}&meal={meal}";
}
