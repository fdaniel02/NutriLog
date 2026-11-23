using NutriLog.Features.Diary;
using NutriLog.Features.Targets;

namespace NutriLog.Tests.Features.Targets;

public class TargetBarTests
{
    static NutrientTotal Complete(double amount) => new(amount, 1, 0);
    static NutrientTotal WithGaps(double amount) => new(amount, 1, 1);

    [Theory]
    [InlineData(10, 0.4)]
    [InlineData(25, 1)]
    [InlineData(60, 1)]
    public void A_minimum_alone_fills_at_the_minimum(double amount, double fill)
    {
        Assert.Equal(new TargetBar(fill, null, amount < 25 ? TargetStatus.Under : TargetStatus.Within),
            TargetBar.For(new TargetRange(25, null), Complete(amount)));
    }

    [Fact]
    public void A_maximum_alone_fills_at_the_maximum()
    {
        Assert.Equal(new TargetBar(0.5, null, TargetStatus.Within), TargetBar.For(new TargetRange(null, 112), Complete(56)));
    }

    [Fact]
    public void A_range_fills_at_its_maximum_and_marks_its_minimum()
    {
        Assert.Equal(new TargetBar(0.25, 0.8, TargetStatus.Under), TargetBar.For(new TargetRange(2000, 2500), Complete(625)));
    }

    [Fact]
    public void Over_fills_the_whole_bar()
    {
        Assert.Equal(new TargetBar(1, 0.8, TargetStatus.Over), TargetBar.For(new TargetRange(2000, 2500), Complete(2600)));
    }

    [Fact]
    public void A_total_with_gaps_fills_its_known_part()
    {
        Assert.Equal(new TargetBar(0.5, null, TargetStatus.Unsure), TargetBar.For(new TargetRange(null, 48), WithGaps(24)));
    }

    [Fact]
    public void An_unknown_total_leaves_the_bar_empty()
    {
        Assert.Equal(new TargetBar(0, null, null), TargetBar.For(new TargetRange(null, 2300), new NutrientTotal(0, 0, 2)));
    }

    [Fact]
    public void A_zero_maximum_is_full_once_known()
    {
        Assert.Equal(new TargetBar(1, null, TargetStatus.Within), TargetBar.For(new TargetRange(null, 0), Complete(0)));
    }
}
