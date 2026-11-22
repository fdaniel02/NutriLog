using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Tests.Features.Diary;

public class NutrientTotalsTests
{
    [Fact]
    public void Scales_the_values_per_100_to_the_amount()
    {
        var totals = NutrientTotals.ForAmount(new Dictionary<string, double> { [Nutrients.Energy] = 250, [Nutrients.Protein] = 10 }, 40);

        Assert.Equal(new NutrientTotal(100, 1, 0), totals[Nutrients.Energy]);
        Assert.Equal(new NutrientTotal(4, 1, 0), totals[Nutrients.Protein]);
    }

    [Fact]
    public void A_nutrient_without_a_value_is_missing_not_zero()
    {
        var total = NutrientTotals.ForAmount(new Dictionary<string, double>(), 100)[Nutrients.Fibre];

        Assert.Equal((0, true, false), (total.Amount, total.Unknown, total.Complete));
    }

    [Fact]
    public void A_sum_with_a_gap_is_incomplete()
    {
        var withFibre = NutrientTotals.ForAmount(new Dictionary<string, double> { [Nutrients.Fibre] = 5 }, 200);
        var withoutFibre = NutrientTotals.ForAmount(new Dictionary<string, double>(), 100);

        var total = NutrientTotals.Sum([withFibre, withoutFibre])[Nutrients.Fibre];

        Assert.Equal(new NutrientTotal(10, 1, 1), total);
        Assert.False(total.Complete);
        Assert.False(total.Unknown);
    }

    [Fact]
    public void Nothing_logged_sums_to_an_empty_total()
    {
        var total = NutrientTotals.Sum([])[Nutrients.Energy];

        Assert.Equal((false, true), (total.HasEntries, total.Complete));
    }

    [Theory]
    [InlineData(0, 0, 0, "0")]
    [InlineData(1234.4, 2, 0, "1234")]
    [InlineData(4.25, 1, 0, "4.3")]
    [InlineData(12.5, 1, 1, "≥ 13")]
    [InlineData(0, 0, 2, "?")]
    public void Formats_a_total(double amount, int known, int missing, string expected)
    {
        Assert.Equal(expected, NutrientFormat.Total(new NutrientTotal(amount, known, missing)));
    }

    [Fact]
    public void An_unknown_total_has_no_unit()
    {
        Assert.Equal(("?", "≥ 3 g"), (NutrientFormat.WithUnit(new NutrientTotal(0, 0, 1), "g"), NutrientFormat.WithUnit(new NutrientTotal(3, 1, 1), "g")));
    }
}
