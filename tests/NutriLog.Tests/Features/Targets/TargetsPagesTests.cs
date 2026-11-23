using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NutriLog.Data;
using NutriLog.Features.Diary;
using NutriLog.Features.Foods;
using NutriLog.Features.Targets;
using NutriLog.Tests.Data;
using NutriLog.Tests.Features.Diary;

namespace NutriLog.Tests.Features.Targets;

[Collection("Postgres")]
public class TargetsPagesTests(PostgresFixture postgres) : BunitContext
{
    static readonly DateOnly Today = DiaryData.Today;
    static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    async Task<(DiaryData Data, NutriLog.Features.Targets.Targets Targets)> SetUpAsync(FakeRunStats? runStats = null)
    {
        var data = await DiaryData.CreateAsync(postgres);
        var targets = new NutriLog.Features.Targets.Targets(data.Factory, runStats ?? new FakeRunStats());
        Services.AddSingleton(data.Clock);
        Services.AddSingleton<IDiary>(data.Diary);
        Services.AddSingleton<IFoods>(new NutriLog.Features.Foods.Foods(data.Factory, data.Time));
        Services.AddSingleton<ITargets>(targets);
        return (data, targets);
    }

    [Fact]
    public async Task Shows_the_saved_targets()
    {
        var (_, targets) = await SetUpAsync();
        await targets.SaveAsync(
        [
            new NutrientTarget { NutrientId = Nutrients.Energy, Minimum = 2400, Maximum = 2600 },
            new NutrientTarget { NutrientId = Nutrients.Protein, Minimum = 140, ExtraPerRunKm = 2 }
        ], Token);

        var page = Render<TargetsPage>();

        page.WaitForElement(".target-row");
        Assert.Equal("2400", page.Find("input[aria-label='Energy minimum']").GetAttribute("value"));
        Assert.Equal("2600", page.Find("input[aria-label='Energy maximum']").GetAttribute("value"));
        Assert.Equal("140", page.Find("input[aria-label='Protein minimum']").GetAttribute("value"));
        Assert.Null(page.Find("input[aria-label='Protein maximum']").GetAttribute("value"));
        Assert.Equal("2", page.Find(".per-km input").GetAttribute("value"));
        Assert.Equal(Nutrients.All.Count, page.FindAll(".target-row").Count);
    }

    [Fact]
    public async Task Saves_the_filled_in_targets()
    {
        var (_, targets) = await SetUpAsync();
        var page = Render<TargetsPage>();
        page.WaitForElement(".target-row");

        page.Find("input[aria-label='Protein minimum']").Change("140");
        page.Find("input[aria-label='Sodium maximum']").Change("2300");
        page.Find(".per-km input").Change("1.5");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Saved.", page.Find(".saved").TextContent));
        var saved = (await targets.GetAsync(Token)).OrderBy(t => t.NutrientId).ToList();
        Assert.Equal([Nutrients.Protein, "sodium"], saved.Select(t => t.NutrientId));
        Assert.Equal((140, (double?)null, 1.5), (saved[0].Minimum, saved[0].Maximum, saved[0].ExtraPerRunKm));
        Assert.Equal(((double?)null, 2300, 0), (saved[1].Minimum, saved[1].Maximum, saved[1].ExtraPerRunKm));
    }

    [Theory]
    [InlineData("Protein minimum", "200", "Protein maximum", "100", "Protein: the minimum is above the maximum.")]
    [InlineData("Fat minimum", "-5", "Fat maximum", "", "Fat: targets can't be negative.")]
    public async Task Refuses_targets_that_make_no_sense(string first, string firstValue, string second, string secondValue, string expected)
    {
        var (_, targets) = await SetUpAsync();
        var page = Render<TargetsPage>();
        page.WaitForElement(".target-row");

        page.Find($"input[aria-label='{first}']").Change(firstValue);
        page.Find($"input[aria-label='{second}']").Change(secondValue);
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(expected, page.Find(".form-error").TextContent));
        Assert.Empty(await targets.GetAsync(Token));
    }

    [Fact]
    public async Task Extra_protein_per_km_needs_a_protein_target()
    {
        var (_, targets) = await SetUpAsync();
        var page = Render<TargetsPage>();
        page.WaitForElement(".target-row");

        page.Find(".per-km input").Change("2");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Extra protein per km needs a protein target to add to.", page.Find(".form-error").TextContent));
        Assert.Empty(await targets.GetAsync(Token));
    }

    [Fact]
    public async Task The_day_shows_its_totals_against_the_run_days_targets()
    {
        var (data, targets) = await SetUpAsync(new FakeRunStats(_ => new RunDay(1, 10, 500, 0)));
        await targets.SaveAsync(
        [
            new NutrientTarget { NutrientId = Nutrients.Energy, Minimum = 2400, Maximum = 2600 },
            new NutrientTarget { NutrientId = Nutrients.Protein, Minimum = 10 },
            new NutrientTarget { NutrientId = Nutrients.Fat, Maximum = 1 },
            new NutrientTarget { NutrientId = "sodium", Maximum = 2300 }
        ], Token);
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));
        await data.Diary.AddEntryAsync(Today, Meal.Breakfast, DiaryData.Noon, bread.Id, new EntryAmount(200, null), Token);

        var page = Render<DayPage>();

        page.WaitForElement(".summary-energy .target");
        Assert.Equal("Run 10 km: +500 kcal.", page.Find(".run-note").TextContent);
        var energy = page.Find(".summary-energy .target");
        Assert.Equal(["target", "under"], energy.ClassList);
        Assert.Equal("under, target 2900–3100", Normalized(energy.TextContent));
        var macros = page.FindAll(".summary-macros .target");
        Assert.Equal(["target within", "target over"], macros.Select(t => t.ClassName));
        Assert.Equal("≥ 10", Normalized(page.Find(".summary-macros .within").LastChild!.TextContent));
        // The bread has no sodium value.
        var sodium = page.FindAll(".nutrient-table .target").Single();
        Assert.Equal("target", string.Join(' ', sodium.ClassList));
        Assert.Contains("not known against", sodium.TextContent);
    }

    [Fact]
    public async Task Without_runstats_the_day_says_it_uses_the_base_targets()
    {
        var (_, targets) = await SetUpAsync(new FakeRunStats(_ => (RunDay?)null));
        await targets.SaveAsync([new NutrientTarget { NutrientId = Nutrients.Energy, Minimum = 2400 }], Token);

        var page = Render<DayPage>();

        page.WaitForAssertion(() => Assert.Equal("RunStats couldn't be reached: base targets.", page.Find(".run-note").TextContent));
        Assert.Equal("under, target ≥ 2400", Normalized(page.Find(".summary-energy .target").TextContent));
    }

    [Fact]
    public async Task Without_targets_the_day_links_to_them()
    {
        await SetUpAsync();

        var page = Render<DayPage>();

        page.WaitForElement(".run-note a");
        Assert.Equal("targets", page.Find(".run-note a").GetAttribute("href"));
        Assert.Empty(page.FindAll(".target"));
    }

    [Fact]
    public async Task The_day_shows_before_a_slow_runstats_answers()
    {
        var runs = new TaskCompletionSource<RunDay?>();
        var (data, targets) = await SetUpAsync(new FakeRunStats(_ => runs.Task));
        await targets.SaveAsync([new NutrientTarget { NutrientId = Nutrients.Energy, Minimum = 2400 }], Token);
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));
        await data.Diary.AddEntryAsync(Today, Meal.Breakfast, DiaryData.Noon, bread.Id, new EntryAmount(100, null), Token);

        var page = Render<DayPage>();

        page.WaitForElement(".meal .entries a");
        Assert.Empty(page.FindAll(".target"));
        Assert.Empty(page.FindAll(".run-note"));

        runs.SetResult(new RunDay(1, 5, 300, 0));

        page.WaitForAssertion(() => Assert.Equal("Run 5 km: +300 kcal.", page.Find(".run-note").TextContent));
        Assert.Equal("under, target ≥ 2700", Normalized(page.Find(".summary-energy .target").TextContent));
    }

    static string Normalized(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
