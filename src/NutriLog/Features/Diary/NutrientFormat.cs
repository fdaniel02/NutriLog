using System.Globalization;

namespace NutriLog.Features.Diary;

public static class NutrientFormat
{
    public static string Amount(double amount) =>
        amount.ToString(Math.Abs(amount) < 10 ? "0.#" : "0", CultureInfo.InvariantCulture);

    // "≥" when some entries have no value for the nutrient, "?" when none has one.
    public static string Total(NutrientTotal total) => total switch
    {
        { Unknown: true } => "?",
        { Complete: false } => $"≥ {Amount(total.Amount)}",
        _ => Amount(total.Amount)
    };

    public static string WithUnit(NutrientTotal total, string unit) =>
        total.Unknown ? "?" : $"{Total(total)} {unit}";
}
