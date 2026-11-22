using Microsoft.EntityFrameworkCore;
using Npgsql;
using NutriLog.Data;
using NutriLog.Features.Targets;
using NutriLog.Tests.Data;

namespace NutriLog.Tests.Features.Targets;

[Collection("Postgres")]
public class TargetsTests(PostgresFixture postgres)
{
    static readonly DateOnly Day = new(2026, 11, 22);
    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Saving_replaces_all_targets()
    {
        var targets = new NutriLog.Features.Targets.Targets(await MigratedDatabase.CreateAsync(postgres), new FakeRunStats());
        await targets.SaveAsync(
        [
            new NutrientTarget { NutrientId = Nutrients.Energy, Minimum = 2400, Maximum = 2600 },
            new NutrientTarget { NutrientId = Nutrients.Fat, Maximum = 90 }
        ], Token);

        await targets.SaveAsync(
        [
            new NutrientTarget { NutrientId = Nutrients.Energy, Minimum = 2500 },
            new NutrientTarget { NutrientId = Nutrients.Protein, Minimum = 140, ExtraPerRunKm = 2 }
        ], Token);

        var saved = (await targets.GetAsync(Token)).OrderBy(t => t.NutrientId).ToList();
        Assert.Equal([Nutrients.Energy, Nutrients.Protein], saved.Select(t => t.NutrientId));
        Assert.Equal((2500, (double?)null), (saved[0].Minimum, saved[0].Maximum));
        Assert.Equal(2, saved[1].ExtraPerRunKm);
    }

    static readonly NutrientTarget[] Invalid =
    [
        new NutrientTarget { NutrientId = "no_such_nutrient", Minimum = 1 },
        new NutrientTarget { NutrientId = Nutrients.Protein },
        new NutrientTarget { NutrientId = Nutrients.Protein, Minimum = -1 },
        new NutrientTarget { NutrientId = Nutrients.Protein, Minimum = 140, ExtraPerRunKm = -2 },
        new NutrientTarget { NutrientId = Nutrients.Protein, Minimum = double.NaN },
        new NutrientTarget { NutrientId = Nutrients.Protein, Minimum = 200, Maximum = 100 }
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Invalid_targets_are_refused(int invalid)
    {
        var targets = new NutriLog.Features.Targets.Targets(await MigratedDatabase.CreateAsync(postgres), new FakeRunStats());

        await Assert.ThrowsAsync<ArgumentException>(() => targets.SaveAsync([Invalid[invalid]], Token));
    }

    [Fact]
    public async Task A_nutrient_has_one_target()
    {
        var targets = new NutriLog.Features.Targets.Targets(await MigratedDatabase.CreateAsync(postgres), new FakeRunStats());

        await Assert.ThrowsAsync<ArgumentException>(() => targets.SaveAsync(
        [
            new NutrientTarget { NutrientId = Nutrients.Protein, Minimum = 140 },
            new NutrientTarget { NutrientId = Nutrients.Protein, Maximum = 200 }
        ], Token));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(-1.0, null)]
    [InlineData(null, -1.0)]
    [InlineData(200.0, 100.0)]
    public async Task The_database_refuses_targets_that_get_past_the_code(double? minimum, double? maximum)
    {
        var factory = await MigratedDatabase.CreateAsync(postgres);
        await using var db = await factory.CreateDbContextAsync(Token);
        db.NutrientTargets.Add(new NutrientTarget { NutrientId = Nutrients.Protein, Minimum = minimum, Maximum = maximum });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task The_day_follows_its_runs()
    {
        var runStats = new FakeRunStats(_ => new RunDay(1, 10, 500, 0));
        var targets = new NutriLog.Features.Targets.Targets(await MigratedDatabase.CreateAsync(postgres), runStats);
        await targets.SaveAsync([new NutrientTarget { NutrientId = Nutrients.Energy, Minimum = 2500 }], Token);

        var day = await targets.GetDayAsync(Day, Token);

        Assert.Equal([Day], runStats.Asked);
        Assert.Equal(new TargetRange(3000, null), day[Nutrients.Energy]);
    }

    [Fact]
    public async Task Without_targets_runstats_isnt_asked()
    {
        var runStats = new FakeRunStats();
        var targets = new NutriLog.Features.Targets.Targets(await MigratedDatabase.CreateAsync(postgres), runStats);

        var day = await targets.GetDayAsync(Day, Token);

        Assert.False(day.HasTargets);
        Assert.Empty(runStats.Asked);
    }
}
