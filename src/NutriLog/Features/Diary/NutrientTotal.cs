using NutriLog.Data;

namespace NutriLog.Features.Diary;

// A sum over entries that counts the entries without a value for the nutrient, so a total of foods with gaps
// reads as "at least", not as the full amount.
public readonly record struct NutrientTotal(double Amount, int Known, int Missing)
{
    public bool HasEntries => Known + Missing > 0;
    public bool Complete => Missing == 0;
    public bool Unknown => Known == 0 && Missing > 0;

    public static NutrientTotal operator +(NutrientTotal a, NutrientTotal b) =>
        new(a.Amount + b.Amount, a.Known + b.Known, a.Missing + b.Missing);
}

// One total per nutrient in Nutrients.All, for an entry, a meal or a day.
public sealed class NutrientTotals
{
    public static readonly NutrientTotals Empty = new(new Dictionary<string, NutrientTotal>());

    readonly IReadOnlyDictionary<string, NutrientTotal> totals;

    NutrientTotals(IReadOnlyDictionary<string, NutrientTotal> totals) => this.totals = totals;

    public NutrientTotal this[string nutrientId] => totals.GetValueOrDefault(nutrientId);

    // amountsPer100 are a food's values per 100 g (or ml); a nutrient without one is unknown for the food.
    public static NutrientTotals ForAmount(IReadOnlyDictionary<string, double> amountsPer100, double grams) =>
        new(Data.Nutrients.All.ToDictionary(n => n.Id, n => amountsPer100.TryGetValue(n.Id, out var per100)
            ? new NutrientTotal(per100 * grams / 100, 1, 0)
            : new NutrientTotal(0, 0, 1)));

    // A recipe entry logged with its nutrients per serving: one entry, complete or not per nutrient.
    public static NutrientTotals ForServings(IReadOnlyDictionary<string, (double PerServing, bool Complete)> perServing, double servings) =>
        new(Data.Nutrients.All.ToDictionary(n => n.Id, n => perServing.TryGetValue(n.Id, out var value)
            ? new NutrientTotal(value.PerServing * servings, 1, value.Complete ? 0 : 1)
            : new NutrientTotal(0, 0, 1)));

    public static NutrientTotals Sum(IEnumerable<NutrientTotals> parts)
    {
        var sum = new Dictionary<string, NutrientTotal>();
        foreach (var part in parts)
            foreach (var (id, total) in part.totals)
                sum[id] = sum.GetValueOrDefault(id) + total;
        return new NutrientTotals(sum);
    }
}
