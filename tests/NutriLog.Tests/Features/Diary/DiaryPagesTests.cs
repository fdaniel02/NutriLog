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
    public async Task The_day_shows_its_meals_entries_and_totals()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3), ("slice", 35));
        var entry = await data.Diary.AddEntryAsync(Today, Meal.Breakfast, bread.Id, new EntryAmount(2, bread.Portions[0].Id), Token);

        var page = Render<DayPage>();

        page.WaitForElement(".meal .entries a");
        Assert.Equal("Today", page.Find("h1").TextContent);
        Assert.Equal(["Breakfast", "Lunch", "Dinner", "Snack"], page.FindAll(".meal h2").Select(h => h.TextContent));
        var link = page.Find(".meal .entries a");
        Assert.Equal(DiaryUrls.Entry(entry), link.GetAttribute("href"));
        Assert.Equal(["Bread", "2 × slice · 70 g", "175 kcal"], link.Children.Select(c => c.TextContent));
        Assert.Equal("175", page.Find(".summary-value").TextContent);
        // The first one, sugars, has no value for the bread.
        Assert.Equal("?", page.Find(".nutrient-table td").TextContent);
        Assert.Equal(DiaryUrls.Add(Today, Meal.Lunch), page.FindAll(".add-link")[1].GetAttribute("href"));
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
    public async Task Copies_yesterdays_meal_into_today()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));
        await data.Diary.AddEntryAsync(Today.AddDays(-1), Meal.Lunch, bread.Id, new EntryAmount(100, null), Token);
        var page = Render<DayPage>();
        page.WaitForElement(".copy select");

        page.Find(".copy select").Change(nameof(Meal.Lunch));
        page.Find(".copy .button").Click();

        page.WaitForAssertion(() => Assert.Equal("Copied 1 entry from Lunch.", page.Find(".copy-message").TextContent));
        Assert.Equal("250", page.Find(".summary-value").TextContent);
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
        var data = await SetUpAsync("add?date=2026-11-22&meal=Lunch");
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));
        var page = Render<AddPage>();
        page.WaitForAssertion(() => Assert.Equal("Search for a food to add.", page.Find(".search-hint").TextContent));

        page.Find("input[type=search]").Input("bre");

        page.WaitForAssertion(() => Assert.Equal(DiaryUrls.Add(Today, Meal.Lunch, bread.Id), page.Find(".matches a").GetAttribute("href")));
        Assert.Equal("NEVO · 250 kcal/100 g", page.Find(".match-details").TextContent.Trim());
        Assert.Equal("Add to Lunch", page.Find("h1").TextContent);
    }

    [Fact]
    public async Task Adds_the_chosen_food_and_returns_to_the_day()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3), ("slice", 35));
        Navigation.NavigateTo(DiaryUrls.Add(Today, Meal.Dinner, bread.Id));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("3");
            page.FindAll("form select")[0].Change(bread.Portions[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.StartsWith("3 × slice · 105 g: 263 kcal",
                string.Join(' ', page.Find(".amount-preview").TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        var day = await data.Diary.GetDayAsync(Today, Token);
        Assert.Equal(["3 × slice · 105 g"], day.Meals.Single(m => m.Meal == Meal.Dinner).Entries.Select(e => e.Amount));
    }

    [Fact]
    public async Task Edits_an_entry()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));
        var id = await data.Diary.AddEntryAsync(Today, Meal.Lunch, bread.Id, new EntryAmount(100, null), Token);
        var page = Render<EntryPage>(p => p.Add(e => e.EntryId, id));
        page.WaitForElement("form.amount-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("50");
            page.FindAll("form select")[1].Change(nameof(Meal.Snack));
            Assert.Equal(("50", nameof(Meal.Snack)), (page.Find("input[type=number]").GetAttribute("value"), page.FindAll("form select")[1].GetAttribute("value")));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        var entry = await data.Diary.GetEntryAsync(id, Token);
        Assert.Equal((Meal.Snack, new EntryAmount(50, null)), (entry!.Meal, entry.Amount));
    }

    [Fact]
    public async Task Deletes_an_entry()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", []);
        var id = await data.Diary.AddEntryAsync(Today, Meal.Lunch, bread.Id, new EntryAmount(100, null), Token);
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
        var data = await SetUpAsync("foods/new?date=2026-11-22&meal=Snack");
        var page = Render<NewFoodPage>();
        page.WaitForElement("form.food-form");

        page.Find("input[type=text]").Change("Crackers");
        page.FindAll("input[type=number]")[0].Change("440");
        page.FindAll("label").Single(l => l.TextContent.Trim().StartsWith("Salt")).QuerySelector("input")!.Change("1.5");
        page.Find(".portions .button").Click();
        page.Find(".portion-row input[type=text]").Change("3 crackers");
        page.Find(".portion-row input[type=number]").Change("20");
        page.Find("form").Submit();

        await using var db = await data.Factory.CreateDbContextAsync(Token);
        var food = await db.Foods.Include(f => f.Nutrients).Include(f => f.Portions).SingleAsync(Token);
        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Add(Today, Meal.Snack, food.Id), Navigation.Uri));
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
