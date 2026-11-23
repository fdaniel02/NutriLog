using NutriLog.Data;

namespace NutriLog.Features.Diary;

// Share is of the energy from the known macros, not of the stated energy, so the shares add up to 1.
// It is null for a macro without a value, and for all of them when the known ones add up to no energy.
public sealed record MacroShare(string NutrientId, string Name, NutrientTotal Grams, double? Share);

public static class MacroEnergy
{
    static readonly (string Id, string Name, double KcalPerGram)[] Macros =
    [
        (Nutrients.Protein, "Protein", 4),
        (Nutrients.Carbohydrate, "Carbohydrate", 4),
        (Nutrients.Fat, "Fat", 9)
    ];

    public static IReadOnlyList<MacroShare> Split(NutrientTotals totals)
    {
        var kcal = Macros.Select(m => totals[m.Id] is { Unknown: false } grams ? grams.Amount * m.KcalPerGram : (double?)null).ToList();
        var sum = kcal.Sum() ?? 0;
        return Macros.Select((m, i) => new MacroShare(m.Id, m.Name, totals[m.Id], sum > 0 ? kcal[i] / sum : null)).ToList();
    }
}
