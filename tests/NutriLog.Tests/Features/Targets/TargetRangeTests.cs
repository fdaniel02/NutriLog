using NutriLog.Features.Diary;
using NutriLog.Features.Targets;

namespace NutriLog.Tests.Features.Targets;

public class TargetRangeTests
{
    static NutrientTotal Complete(double amount) => new(amount, 1, 0);
    static NutrientTotal WithGaps(double amount) => new(amount, 1, 1);

    [Theory]
    [InlineData(100, TargetStatus.Under)]
    [InlineData(140, TargetStatus.Within)]
    [InlineData(500, TargetStatus.Within)]
    public void A_minimum_is_met_from_its_value_on(double amount, TargetStatus expected)
    {
        Assert.Equal(expected, new TargetRange(140, null).Evaluate(Complete(amount)));
    }

    [Theory]
    [InlineData(2300, TargetStatus.Within)]
    [InlineData(2301, TargetStatus.Over)]
    public void A_maximum_is_exceeded_above_its_value(double amount, TargetStatus expected)
    {
        Assert.Equal(expected, new TargetRange(null, 2300).Evaluate(Complete(amount)));
    }

    [Theory]
    [InlineData(1900, TargetStatus.Under)]
    [InlineData(2600, TargetStatus.Within)]
    [InlineData(2800, TargetStatus.Over)]
    public void A_range_has_both_ends(double amount, TargetStatus expected)
    {
        Assert.Equal(expected, new TargetRange(2500, 2700).Evaluate(Complete(amount)));
    }

    [Fact]
    public void A_total_with_gaps_is_only_a_lower_bound()
    {
        // Over is certain, reaching a minimum is too; staying under a maximum and missing a minimum aren't.
        Assert.Equal(TargetStatus.Over, new TargetRange(null, 2300).Evaluate(WithGaps(2400)));
        Assert.Equal(TargetStatus.Within, new TargetRange(140, null).Evaluate(WithGaps(150)));
        Assert.Equal(TargetStatus.Unsure, new TargetRange(null, 2300).Evaluate(WithGaps(1000)));
        Assert.Equal(TargetStatus.Unsure, new TargetRange(140, null).Evaluate(WithGaps(100)));
    }

    [Fact]
    public void A_nutrient_no_food_has_a_value_for_has_no_status()
    {
        Assert.Null(new TargetRange(140, null).Evaluate(new NutrientTotal(0, 0, 2)));
    }

    [Fact]
    public void A_day_without_entries_is_under_its_minimum()
    {
        Assert.Equal(TargetStatus.Under, new TargetRange(140, null).Evaluate(default));
    }

    [Fact]
    public void Describes_each_kind_of_target()
    {
        Assert.Equal("≥ 140", new TargetRange(140, null).Describe());
        Assert.Equal("≤ 2300", new TargetRange(null, 2300).Describe());
        Assert.Equal("2500–2700", new TargetRange(2500, 2700).Describe());
        Assert.Equal("", default(TargetRange).Describe());
    }

    [Fact]
    public void The_reference_is_the_middle_of_a_range_or_its_only_end()
    {
        Assert.Equal(2600, new TargetRange(2500, 2700).Reference);
        Assert.Equal(2500, new TargetRange(2500, null).Reference);
        Assert.Equal(2700, new TargetRange(null, 2700).Reference);
        Assert.Null(default(TargetRange).Reference);
    }

    [Fact]
    public void Shifting_and_scaling_leave_an_open_end_open()
    {
        Assert.Equal(new TargetRange(150, null), new TargetRange(140, null).Plus(10));
        Assert.Equal(new TargetRange(null, 300), new TargetRange(null, 250).Times(1.2));
    }
}
