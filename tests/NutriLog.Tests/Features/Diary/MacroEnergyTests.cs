using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Tests.Features.Diary;

public class MacroEnergyTests
{
    static NutrientTotals Per100(Dictionary<string, double> values) => NutrientTotals.ForAmount(values, 100);

    [Fact]
    public void Splits_the_energy_from_the_macros_at_4_4_and_9_kcal_per_gram()
    {
        var split = MacroEnergy.Split(Per100(new() { [Nutrients.Protein] = 5.3, [Nutrients.Carbohydrate] = 1.6, [Nutrients.Fat] = 0.7 }));

        Assert.Equal([Nutrients.Protein, Nutrients.Carbohydrate, Nutrients.Fat], split.Select(m => m.NutrientId));
        Assert.Equal([0.63, 0.19, 0.19], split.Select(m => Math.Round(m.Share!.Value, 2)));
        Assert.Equal(1, split.Sum(m => m.Share!.Value), 9);
        Assert.Equal(5.3, split[0].Grams.Amount, 9);
    }

    [Fact]
    public void A_macro_without_a_value_has_no_share()
    {
        var split = MacroEnergy.Split(Per100(new() { [Nutrients.Protein] = 10, [Nutrients.Fat] = 0 }));

        Assert.Equal([1.0, null, 0.0], split.Select(m => m.Share));
        Assert.True(split[1].Grams.Unknown);
    }

    [Fact]
    public void Without_energy_from_macros_none_has_a_share()
    {
        Assert.All(MacroEnergy.Split(Per100(new() { [Nutrients.Protein] = 0 })), m => Assert.Null(m.Share));
        Assert.All(MacroEnergy.Split(Per100([])), m => Assert.Null(m.Share));
    }
}
