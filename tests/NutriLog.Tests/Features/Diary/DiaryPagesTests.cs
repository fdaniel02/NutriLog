using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NutriLog.Data;
using NutriLog.Features.Diary;
using NutriLog.Features.Foods;
using NutriLog.Features.Targets;
using NutriLog.Tests.Data;

namespace NutriLog.Tests.Features.Diary;

// The pages with the real diary on a database, as the services they inject. Form changes are retried until they
// show: an input changed right after the page appears is sometimes rendered over by a late render of the page.
[Collection("Postgres")]
public class DiaryPagesTests(PostgresFixture postgres) : BunitContext
{
    static readonly DateOnly Today = DiaryData.Today;
    static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    async Task<DiaryData> SetUpAsync(string url = "")
    {
        var data = await DiaryData.CreateAsync(postgres);
        Services.AddSingleton(data.Clock);
        Services.AddSingleton<IDiary>(data.Diary);
        Services.AddSingleton<IFoods>(new NutriLog.Features.Foods.Foods(data.Factory, data.Time));
        Services.AddSingleton<NutriLog.Features.Recipes.IRecipes>(new NutriLog.Features.Recipes.Recipes(data.Factory, data.Time));
        Services.AddSingleton<ITargets>(new NutriLog.Features.Targets.Targets(data.Factory, new NutriLog.Tests.Features.Targets.FakeRunStats()));
        Navigation.NavigateTo(url);
        return data;
    }

    NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public async Task The_day_shows_one_list_of_its_entries_and_totals()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3), ("slice", 35));
        var entry = await data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(2, bread.Portions[0].Id), Token);
        await data.Diary.AddEntryAsync(Today, new TimeOnly(7, 15), bread.Id, new EntryAmount(100, null), Token);

        var page = Render<DayPage>();

        page.WaitForElement(".day-entries .entry-link");
        Assert.Equal("Today", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll("h2"));
        var links = page.FindAll(".day-entries .entry-link");
        Assert.Equal(["07:15 · 100 g", "12:00 · 2 × slice · 70 g"], links.Select(l => l.QuerySelector(".entry-amount")!.TextContent));
        Assert.Equal(DiaryUrls.Entry(entry), links[1].GetAttribute("href"));
        Assert.Equal(["Bread", "12:00 · 2 × slice · 70 g", "P 6.3 g · C 32 g · F 2.1 g", "175 kcal"], links[1].Children.Select(c => c.TextContent));
        Assert.Equal(
            [DiaryUrls.AddAgain(Today, new LoggedAmount(bread.Id, null, 100, null)), DiaryUrls.AddAgain(Today, new LoggedAmount(bread.Id, null, 2, bread.Portions[0].Id))],
            page.FindAll(".add-again").Select(a => a.GetAttribute("href")));
        Assert.Equal("Add Bread again", page.Find(".add-again").GetAttribute("aria-label"));
        Assert.Equal("425", page.Find(".summary-value").TextContent);
        // The first one, sugars, has no value for the bread.
        Assert.Equal("?", page.Find(".nutrient-table td").TextContent);
        var add = Assert.Single(page.FindAll(".add-link"));
        Assert.Equal("+ Add food or recipe", add.TextContent);
        Assert.Equal(DiaryUrls.Add(Today), add.GetAttribute("href"));
        Assert.Equal(DiaryUrls.Day(Today.AddDays(-1)), page.Find("a[aria-label='Previous day']").GetAttribute("href"));
    }

    [Fact]
    public async Task The_day_follows_the_date_in_the_url()
    {
        await SetUpAsync("?date=2026-11-18");

        var page = Render<DayPage>();

        page.WaitForAssertion(() => Assert.Equal("Wed 18 Nov", page.Find("h1").TextContent));
    }

    [Fact]
    public async Task Choosing_a_date_navigates_to_it()
    {
        await SetUpAsync();
        var page = Render<DayPage>();
        page.WaitForElement("h1");

        page.Find("input[aria-label='Go to date']").Change("2026-10-01");

        Assert.EndsWith("?date=2026-10-01", Navigation.Uri);
    }

    [Fact]
    public async Task Copies_yesterday_into_today()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));
        await data.Diary.AddEntryAsync(Today.AddDays(-1), new TimeOnly(8, 0), bread.Id, new EntryAmount(100, null), Token);
        await data.Diary.AddEntryAsync(Today.AddDays(-1), DiaryData.Noon, bread.Id, new EntryAmount(100, null), Token);
        var page = Render<DayPage>();
        page.WaitForElement(".copy .button");

        Assert.Empty(page.FindAll(".copy select"));
        page.Find(".copy .button").Click();

        page.WaitForAssertion(() => Assert.Equal("Copied 2 entries.", page.Find(".copy-message").TextContent));
        Assert.Equal("500", page.Find(".summary-value").TextContent);
        Assert.Equal(2, page.FindAll(".day-entries .entry-link").Count);
    }

    [Fact]
    public async Task Copying_from_an_empty_day_says_so()
    {
        await SetUpAsync();
        var page = Render<DayPage>();
        page.WaitForElement(".copy .button");

        page.Find(".copy .button").Click();

        page.WaitForAssertion(() => Assert.Equal("Nothing to copy on Yesterday.", page.Find(".copy-message").TextContent));
    }

    [Fact]
    public async Task Search_lists_matches_that_link_to_their_amount_form()
    {
        var data = await SetUpAsync("add?date=2026-11-22");
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));
        var page = Render<AddPage>();
        page.WaitForAssertion(() => Assert.Equal("Search for a food to add.", page.Find(".search-hint").TextContent));

        page.Find("input[type=search]").Input("bre");

        page.WaitForAssertion(() => Assert.Equal(DiaryUrls.Add(Today, bread.Id), page.Find(".matches a").GetAttribute("href")));
        Assert.Equal("NEVO · 250 kcal/100 g", page.Find(".match-details").TextContent.Trim());
        Assert.Equal("Add to today", page.Find("h1").TextContent);
    }

    [Fact]
    public async Task Adds_the_chosen_food_and_returns_to_the_day()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3), ("slice", 35));
        Navigation.NavigateTo(DiaryUrls.Add(Today, bread.Id));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");

        Assert.Equal("10:00:00", page.Find("input[type=time]").GetAttribute("value"));

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("3");
            page.FindAll("form select")[0].Change(bread.Portions[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // Blazor's script in the browser sends a time input's value with seconds.
            page.Find("input[type=time]").Change("08:05:00");
            Assert.StartsWith("3 × slice · 105 g: 263 kcal",
                string.Join(' ', page.Find(".amount-preview").TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
            Assert.Equal("08:05:00", page.Find("input[type=time]").GetAttribute("value"));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        var day = await data.Diary.GetDayAsync(Today, Token);
        Assert.Equal([("3 × slice · 105 g", new TimeOnly(8, 5))], day.Entries.Select(e => (e.Amount, e.Time)));
    }

    [Fact]
    public async Task The_time_starts_at_now_also_for_another_day()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", []);
        Navigation.NavigateTo(DiaryUrls.Add(Today.AddDays(-3), bread.Id));

        var page = Render<AddPage>();

        page.WaitForElement("form.amount-form");
        Assert.Equal("10:00:00", page.Find("input[type=time]").GetAttribute("value"));
    }

    [Fact]
    public async Task The_date_starts_at_today_also_for_another_day()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", []);
        Navigation.NavigateTo(DiaryUrls.Add(Today.AddDays(-3), bread.Id));

        var page = Render<AddPage>();

        page.WaitForElement("form.amount-form");
        Assert.Equal("2026-11-22", page.Find("input[type=date]").GetAttribute("value"));
        Assert.Equal("Add to today", page.Find("h1").TextContent);
        Assert.Equal(DiaryUrls.Day(Today.AddDays(-3)), page.Find(".back-nav a").GetAttribute("href"));
    }

    [Fact]
    public async Task Adding_again_from_yesterday_adds_to_today()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", [], ("slice", 35));
        var yesterday = Today.AddDays(-1);
        await data.Diary.AddEntryAsync(yesterday, DiaryData.Noon, bread.Id, new EntryAmount(2, bread.Portions[0].Id), Token);
        Navigation.NavigateTo(DiaryUrls.AddAgain(yesterday, new LoggedAmount(bread.Id, null, 2, bread.Portions[0].Id)));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        Assert.Equal(["2 × slice · 70 g"], (await data.Diary.GetDayAsync(Today, Token)).Entries.Select(e => e.Amount));
        Assert.Single((await data.Diary.GetDayAsync(yesterday, Token)).Entries);
    }

    [Fact]
    public async Task Adds_to_the_chosen_date()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", []);
        Navigation.NavigateTo(DiaryUrls.Add(Today, bread.Id));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=date]").Change("2026-11-20");
            Assert.Equal("Add to fri 20 nov", page.Find("h1").TextContent);
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(new DateOnly(2026, 11, 20)), Navigation.Uri));
        Assert.Single((await data.Diary.GetDayAsync(new DateOnly(2026, 11, 20), Token)).Entries);
        Assert.Empty((await data.Diary.GetDayAsync(Today, Token)).Entries);
    }

    [Fact]
    public async Task An_empty_date_disables_adding()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", []);
        Navigation.NavigateTo(DiaryUrls.Add(Today, bread.Id));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=date]").Change("");
            Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        });
        Assert.Equal("Add food", page.Find("h1").TextContent);
        page.Find("form").Submit();

        Assert.Empty((await data.Diary.GetDayAsync(Today, Token)).Entries);
    }

    [Fact]
    public async Task Adding_again_starts_at_the_entrys_amount_and_unit()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3), ("slice", 35));
        await data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(80, null), Token);
        Navigation.NavigateTo(DiaryUrls.AddAgain(Today, new LoggedAmount(bread.Id, null, 3, bread.Portions[0].Id)));

        var page = Render<AddPage>();

        page.WaitForElement("form.amount-form");
        Assert.Equal(("3", bread.Portions[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "10:00:00"),
            (page.Find("input[type=number]").GetAttribute("value"), page.Find("form select").GetAttribute("value"), page.Find("input[type=time]").GetAttribute("value")));
    }

    [Theory]
    [InlineData("&amount=3&portion=999999")]
    [InlineData("&amount=0")]
    [InlineData("&amount=-2")]
    public async Task Adding_again_with_an_amount_that_does_not_fit_starts_at_the_last_one(string query)
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", [], ("slice", 35));
        await data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(80, null), Token);
        Navigation.NavigateTo(DiaryUrls.Add(Today, bread.Id) + query);

        var page = Render<AddPage>();

        page.WaitForElement("form.amount-form");
        Assert.Equal(("80", ""), (page.Find("input[type=number]").GetAttribute("value"), page.Find("form select").GetAttribute("value")));
    }

    [Fact]
    public async Task The_form_shows_the_nutrition_of_the_amount_with_the_energy_split()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 10, 40, 4), ("slice", 35));
        var id = await data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(100, null), Token);
        var page = Render<EntryPage>(p => p.Add(e => e.EntryId, id));
        page.WaitForElement(".nutrition");

        // 10 g protein and 40 g carbohydrate at 4 kcal, 4 g fat at 9: 40 + 160 + 36 = 236 kcal from macros.
        Assert.Equal("Nutrition for 100 g", page.Find(".nutrition h2").TextContent);
        Assert.Equal(["Protein (17%) 10 g", "Carbohydrate (68%) 40 g", "Fat (15%) 4 g"],
            page.FindAll(".macro-legend li").Select(li => Squeezed(li.TextContent)));
        Assert.Equal(3, page.FindAll(".donut-segment").Count);
        Assert.Equal("250 kcal", page.Find(".energy-donut").GetAttribute("aria-label"));
        Assert.DoesNotContain(page.FindAll(".nutrition .nutrient-table th"), th => th.TextContent is "Protein" or "Energy");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("50");
            Assert.Equal("Nutrition for 50 g", page.Find(".nutrition h2").TextContent);
        });
        Assert.Equal("125 kcal", page.Find(".energy-donut").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Without_macros_there_is_no_chart()
    {
        var data = await SetUpAsync();
        var tea = await data.AddFoodAsync("Tea", new Dictionary<string, double> { [Nutrients.Energy] = 1 });
        var id = await data.Diary.AddEntryAsync(Today, DiaryData.Noon, tea.Id, new EntryAmount(200, null), Token);

        var page = Render<EntryPage>(p => p.Add(e => e.EntryId, id));

        page.WaitForElement(".nutrition");
        Assert.Empty(page.FindAll(".energy-donut"));
        Assert.Equal("2 kcal", Squeezed(page.Find(".energy-only").TextContent));
        Assert.Equal(["Protein ?", "Carbohydrate ?", "Fat ?"], page.FindAll(".macro-legend li").Select(li => Squeezed(li.TextContent)));
    }

    static string Squeezed(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public async Task An_empty_time_disables_adding()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", []);
        Navigation.NavigateTo(DiaryUrls.Add(Today, bread.Id));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=time]").Change("");
            Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        });
        page.Find("form").Submit();

        Assert.Empty((await data.Diary.GetDayAsync(Today, Token)).Entries);
    }

    [Fact]
    public async Task Edits_an_entry()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));
        var id = await data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(100, null), Token);
        var page = Render<EntryPage>(p => p.Add(e => e.EntryId, id));
        page.WaitForElement("form.amount-form");

        Assert.Equal("12:00:00", page.Find("input[type=time]").GetAttribute("value"));
        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("50");
            page.Find("input[type=time]").Change("16:20:00");
            Assert.Equal(("50", "16:20:00"), (page.Find("input[type=number]").GetAttribute("value"), page.Find("input[type=time]").GetAttribute("value")));
        });
        Assert.Single(page.FindAll("form select"));
        Assert.Empty(page.FindAll("input[type=date]"));
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        var entry = await data.Diary.GetEntryAsync(id, Token);
        Assert.Equal((new TimeOnly(16, 20), new EntryAmount(50, null)), (entry!.Time, entry.Amount));
    }

    [Fact]
    public async Task Deletes_an_entry()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", []);
        var id = await data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(100, null), Token);
        var page = Render<EntryPage>(p => p.Add(e => e.EntryId, id));
        page.WaitForElement("button.danger");

        page.Find("button.danger").Click();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        Assert.Null(await data.Diary.GetEntryAsync(id, Token));
    }

    [Fact]
    public async Task An_unknown_entry_says_so()
    {
        await SetUpAsync();

        var page = Render<EntryPage>(p => p.Add(e => e.EntryId, 12345));

        page.WaitForAssertion(() => Assert.Equal("Entry not found", page.Find("h1").TextContent));
    }

    [Fact]
    public async Task A_new_food_is_saved_with_salt_as_sodium_and_then_added()
    {
        var data = await SetUpAsync("foods/new?date=2026-11-22");
        var page = Render<NewFoodPage>();
        page.WaitForElement("form.food-form");

        page.Find("input[type=text]").Change("Crackers");
        page.FindAll("input[type=number]")[0].Change("440");
        page.FindAll("label").Single(l => l.TextContent.Trim().StartsWith("Salt")).QuerySelector("input")!.Change("1.5");
        page.Find(".portions .button").Click();
        page.Find(".portion-row input[type=text]").Change("3 crackers");
        page.Find(".portion-row input[type=number]").Change("20");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("food=", Navigation.Uri));
        await using var db = await data.Factory.CreateDbContextAsync(Token);
        var food = await db.Foods.Include(f => f.Nutrients).Include(f => f.Portions).SingleAsync(Token);
        Assert.EndsWith(DiaryUrls.Add(Today, food.Id), Navigation.Uri);
        Assert.Equal([(Nutrients.Energy, 440.0), ("sodium", 600.0)], food.Nutrients.OrderBy(n => n.NutrientId).Select(n => (n.NutrientId, n.Amount)));
        Assert.Equal(("3 crackers", 20.0), (food.Portions[0].Name, food.Portions[0].Grams));
    }

    [Fact]
    public async Task A_half_filled_portion_is_not_saved()
    {
        var data = await SetUpAsync("foods/new");
        var page = Render<NewFoodPage>();
        page.WaitForElement("form.food-form");

        page.Find("input[type=text]").Change("Crackers");
        page.Find(".portions .button").Click();
        page.Find(".portion-row input[type=text]").Change("3 crackers");
        page.Find("form").Submit();

        Assert.Equal("Each portion needs a name and a size in g.", page.Find(".form-error").TextContent);
        await using var db = await data.Factory.CreateDbContextAsync(Token);
        Assert.False(await db.Foods.AnyAsync(Token));
    }

    [Fact]
    public async Task A_food_that_fails_validation_shows_why()
    {
        await SetUpAsync("foods/new");
        var page = Render<NewFoodPage>();
        page.WaitForElement("form.food-form");

        page.Find("input[type=text]").Change("   ");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.StartsWith("The name must be", page.Find(".form-error").TextContent));
    }
}
