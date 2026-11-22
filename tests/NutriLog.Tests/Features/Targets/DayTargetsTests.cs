using NutriLog.Data;
using NutriLog.Features.Targets;

namespace NutriLog.Tests.Features.Targets;

public class DayTargetsTests
{
    static readonly NutrientTarget[] Base =
    [
        new() { NutrientId = Nutrients.Energy, Minimum = 2400, Maximum = 2600 },
        new() { NutrientId = Nutrients.Carbohydrate, Minimum = 250, Maximum = 300 },
        new() { NutrientId = Nutrients.Protein, Minimum = 140, ExtraPerRunKm = 2 },
        new() { NutrientId = "sodium", Maximum = 2300 }
    ];

    [Fact]
    public void A_day_without_runs_has_the_base_targets()
    {
        var day = DayTargets.For(Base, RunDay.None);

        Assert.Equal(new TargetRange(2400, 2600), day[Nutrients.Energy]);
        Assert.Equal(new TargetRange(250, 300), day[Nutrients.Carbohydrate]);
        Assert.Equal(new TargetRange(140, null), day[Nutrients.Protein]);
        Assert.Equal(new TargetRange(null, 2300), day["sodium"]);
        Assert.Null(day[Nutrients.Fat]);
    }

    [Fact]
    public void A_run_adds_its_calories_carbs_in_proportion_and_protein_per_km()
    {
        // 650 kcal on a base of 2500 (the middle of the range): 26 % more.
        var day = DayTargets.For(Base, new RunDay(1, 10, 650, 0));

        Assert.Equal(new TargetRange(3050, 3250), day[Nutrients.Energy]);
        var carbs = day[Nutrients.Carbohydrate]!.Value;
        Assert.Equal(315, carbs.Minimum!.Value, 6);
        Assert.Equal(378, carbs.Maximum!.Value, 6);
        Assert.Equal(new TargetRange(160, null), day[Nutrients.Protein]);
        Assert.Equal(new TargetRange(null, 2300), day["sodium"]);
    }

    [Fact]
    public void Without_an_energy_target_carbs_stay_as_they_are()
    {
        var day = DayTargets.For([new NutrientTarget { NutrientId = Nutrients.Carbohydrate, Minimum = 250 }], new RunDay(1, 10, 650, 0));

        Assert.Equal(new TargetRange(250, null), day[Nutrients.Carbohydrate]);
    }

    [Fact]
    public void Unreachable_runstats_gives_the_base_targets_and_says_so()
    {
        var day = DayTargets.For(Base, null);

        Assert.Equal(new TargetRange(2400, 2600), day[Nutrients.Energy]);
        Assert.Equal("RunStats couldn't be reached: base targets.", day.RunNote);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, "No runs: base targets.")]
    [InlineData(1, 10.24, 650, 0, "Run 10 km: +650 kcal.")]
    [InlineData(2, 8.5, 500, 1, "2 runs 8.5 km: +500 kcal (1 without calories).")]
    public void The_run_note_sums_up_the_day(int runs, double km, double calories, int withoutCalories, string expected)
    {
        Assert.Equal(expected, DayTargets.For(Base, new RunDay(runs, km, calories, withoutCalories)).RunNote);
    }

    [Fact]
    public void No_targets_means_nothing_to_show()
    {
        Assert.False(DayTargets.Empty.HasTargets);
        Assert.True(DayTargets.For(Base, RunDay.None).HasTargets);
    }
}
