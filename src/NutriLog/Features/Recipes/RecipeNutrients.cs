using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Features.Recipes;

public static class RecipeNutrients
{
    // Per nutrient that some ingredient has a value for: the amount in one serving, and whether every ingredient
    // has a value (otherwise the amount is a lower bound).
    public static IReadOnlyDictionary<string, (double PerServing, bool Complete)> Snapshot(
        IEnumerable<(IReadOnlyDictionary<string, double> Per100, double Grams)> ingredients, double servings)
    {
        var sum = NutrientTotals.Sum(ingredients.Select(i => NutrientTotals.ForAmount(i.Per100, i.Grams)));
        return Nutrients.All
            .Select(n => (n.Id, Total: sum[n.Id]))
            .Where(n => n.Total.Known > 0)
            .ToDictionary(n => n.Id, n => (n.Total.Amount / servings, n.Total.Complete));
    }
}
