using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NutriLog.Data;
using NutriLog.Features.Diary;
using NutriLog.Features.Recipes;
using NutriLog.Tests.Data;
using NutriLog.Tests.Features.Diary;

namespace NutriLog.Tests.Features.Recipes;

[Collection("Postgres")]
public class RecipePagesTests(PostgresFixture postgres) : BunitContext
{
    static readonly DateOnly Today = DiaryData.Today;
    static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    sealed record Setup(DiaryData Data, IRecipes Recipes, Food Oats, Food Milk);

    async Task<Setup> SetUpAsync(string url = "")
    {
        var data = await DiaryData.CreateAsync(postgres);
        var recipes = new NutriLog.Features.Recipes.Recipes(data.Factory, data.Time);
        Services.AddSingleton(data.Clock);
        Services.AddSingleton<IDiary>(data.Diary);
        Services.AddSingleton<IRecipes>(recipes);
        Navigation.NavigateTo(url);
        var oats = await data.AddFoodAsync("Oats", DiaryData.Macros(370, 13, 60, 7));
        var milk = await data.AddFoodAsync("Milk", DiaryData.Macros(60, 3.4, 4.7, 3.3));
        return new Setup(data, recipes, oats, milk);
    }

    NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    static RecipeInput Porridge(Setup s) => new("Porridge", 2, [new(s.Oats.Id, 100), new(s.Milk.Id, 400)]);

    static string Squeezed(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public async Task The_list_links_each_recipe_with_its_energy_per_serving()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);

        var page = Render<RecipesPage>();

        page.WaitForElement(".matches a");
        Assert.Equal(RecipeUrls.Edit(id), page.Find(".matches a").GetAttribute("href"));
        Assert.Equal("2 servings · 305 kcal per serving", page.Find(".match-details").TextContent);
        page.Find("input[type=search]").Input("soup");
        page.WaitForAssertion(() => Assert.Equal("No recipe found.", page.Find(".search-hint").TextContent));
    }

    [Fact]
    public async Task Without_recipes_the_list_says_so()
    {
        await SetUpAsync();

        var page = Render<RecipesPage>();

        page.WaitForAssertion(() => Assert.Equal("No recipes yet.", page.Find(".search-hint").TextContent));
        Assert.Equal(RecipeUrls.New, page.Find(".add-link").GetAttribute("href"));
    }

    [Fact]
    public async Task A_new_recipe_is_built_from_searched_foods()
    {
        var s = await SetUpAsync(RecipeUrls.New);
        var page = Render<RecipeEditPage>();
        page.WaitForElement("form.recipe-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=text]").Change("Porridge");
            page.FindAll("input[type=number]")[0].Change("2");
            Assert.Equal(("Porridge", "2"), (page.Find("input[type=text]").GetAttribute("value"), page.FindAll("input[type=number]")[0].GetAttribute("value")));
        });
        page.Find("input[type=search]").Input("oat");
        page.WaitForElement(".match-button").Click();
        page.WaitForElement("input[aria-label='Amount of Oats']");
        page.Find("input[type=search]").Input("milk");
        page.WaitForElement(".match-button").Click();
        page.WaitForElement("input[aria-label='Amount of Milk']");
        page.WaitForAssertion(() =>
        {
            page.Find("input[aria-label='Amount of Milk']").Change("400");
            Assert.Equal("305", page.Find(".summary-value").TextContent);
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(RecipeUrls.List, Navigation.Uri));
        var recipe = (await s.Recipes.SearchAsync("", Token)).Single();
        var details = await s.Recipes.GetAsync(recipe.Id, Token);
        Assert.Equal([("Milk", 400.0), ("Oats", 100.0)], details!.Ingredients.Select(i => (i.Name, i.Grams)));
    }

    [Fact]
    public async Task A_recipe_without_foods_shows_why_it_is_not_saved()
    {
        await SetUpAsync(RecipeUrls.New);
        var page = Render<RecipeEditPage>();
        page.WaitForElement("form.recipe-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=text]").Change("Nothing");
            Assert.Equal("Nothing", page.Find("input[type=text]").GetAttribute("value"));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Add at least one food.", page.Find(".form-error").TextContent));
    }

    [Fact]
    public async Task A_food_without_an_amount_is_not_saved()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        var page = Render<RecipeEditPage>(p => p.Add(r => r.RecipeId, id));
        page.WaitForElement("form.recipe-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[aria-label='Amount of Oats']").Change("");
            Assert.True(string.IsNullOrEmpty(page.Find("input[aria-label='Amount of Oats']").GetAttribute("value")));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("Each food needs an amount, and the recipe a number of servings.", page.Find(".form-error").TextContent));
    }

    [Fact]
    public async Task An_existing_recipe_is_edited_and_a_food_removed()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        var page = Render<RecipeEditPage>(p => p.Add(r => r.RecipeId, id));
        page.WaitForElement("form.recipe-form");
        Assert.Equal("Edit recipe", page.Find("h1").TextContent);

        page.Find("button[aria-label='Remove Milk']").Click();
        page.WaitForAssertion(() =>
        {
            page.FindAll("input[type=number]")[0].Change("1");
            Assert.Equal("1", page.FindAll("input[type=number]")[0].GetAttribute("value"));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(RecipeUrls.List, Navigation.Uri));
        var recipe = await s.Recipes.GetAsync(id, Token);
        Assert.Equal((1.0, 1), (recipe!.Servings, recipe.Ingredients.Count));
    }

    [Fact]
    public async Task A_recipe_in_the_diary_is_not_deleted()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        await s.Recipes.LogAsync(Today, DiaryData.Noon, id, RecipeAmount.InServings(1), Token);
        var page = Render<RecipeEditPage>(p => p.Add(r => r.RecipeId, id));
        page.WaitForElement("button.danger");

        page.Find("button.danger").Click();

        page.WaitForAssertion(() => Assert.StartsWith("This recipe is in the diary", page.Find(".form-error").TextContent));
    }

    [Fact]
    public async Task An_unused_recipe_is_deleted()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        var page = Render<RecipeEditPage>(p => p.Add(r => r.RecipeId, id));
        page.WaitForElement("button.danger");

        page.Find("button.danger").Click();

        page.WaitForAssertion(() => Assert.EndsWith(RecipeUrls.List, Navigation.Uri));
        Assert.Null(await s.Recipes.GetAsync(id, Token));
    }

    [Fact]
    public async Task An_unknown_recipe_says_so()
    {
        await SetUpAsync();

        var page = Render<RecipeEditPage>(p => p.Add(r => r.RecipeId, 12345));

        page.WaitForAssertion(() => Assert.Equal("Recipe not found", page.Find("h1").TextContent));
    }

    [Fact]
    public async Task Searching_in_the_diary_lists_matching_recipes()
    {
        var s = await SetUpAsync("add?date=2026-11-22");
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        var page = Render<AddPage>();
        page.WaitForElement("input[type=search]");
        Assert.Empty(page.FindAll(".recipe-matches"));

        page.Find("input[type=search]").Input("porr");

        page.WaitForAssertion(() => Assert.Equal(RecipeUrls.AddToDay(Today, id), page.Find(".recipe-matches a").GetAttribute("href")));
        Assert.Equal("Recipe · 305 kcal/serving", page.Find(".recipe-matches .match-details").TextContent);
    }

    [Fact]
    public async Task A_recipe_is_added_to_a_day_by_servings()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        Navigation.NavigateTo(RecipeUrls.AddToDay(Today, id));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("1.5");
            Assert.StartsWith("1.5 servings · 375 g: 458 kcal", Squeezed(page.Find(".amount-preview").TextContent));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        var day = await s.Data.Diary.GetDayAsync(Today, Token);
        Assert.Equal([("Porridge", "1.5 servings", new TimeOnly(10, 0))], day.Entries.Select(e => (e.Name, e.Amount, e.Time)));
        Assert.Equal(new LoggedAmount(null, id, 1.5, null, RecipeUnit.Servings), day.Entries[0].Logged);
    }

    [Fact]
    public async Task A_recipe_is_added_to_the_chosen_date()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        var yesterday = Today.AddDays(-1);
        Navigation.NavigateTo(RecipeUrls.AddToDay(yesterday, id));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");
        Assert.Equal("2026-11-22", page.Find("input[type=date]").GetAttribute("value"));

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=date]").Change("2026-11-21");
            Assert.Equal("Add to yesterday", page.Find("h1").TextContent);
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(yesterday), Navigation.Uri));
        Assert.Single((await s.Data.Diary.GetDayAsync(yesterday, Token)).Entries);
    }

    [Theory]
    [InlineData(1.5, RecipeUnit.Servings, "1.5", "Servings", "1.5 servings · 375 g")]
    [InlineData(0, RecipeUnit.Servings, "1", "Servings", "1 serving · 250 g")]
    [InlineData(300, RecipeUnit.Grams, "300", "Grams", "300 g · 1.2 servings")]
    [InlineData(0, RecipeUnit.Grams, "1", "Servings", "1 serving · 250 g")]
    public async Task Adding_a_recipe_again_starts_at_the_entrys_amount(double quantity, RecipeUnit unit, string expected, string expectedUnit, string caption)
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        Navigation.NavigateTo(DiaryUrls.AddAgain(Today, new LoggedAmount(null, id, quantity, null, unit)));

        var page = Render<AddPage>();

        page.WaitForElement("form.amount-form");
        Assert.Equal((expected, expectedUnit), (page.Find("input[type=number]").GetAttribute("value"), page.Find("select").GetAttribute("value")));
        Assert.Equal($"Nutrition for {caption}", page.Find(".nutrition h2").TextContent);
    }

    [Fact]
    public async Task A_recipe_is_added_to_a_day_by_grams()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        Navigation.NavigateTo(RecipeUrls.AddToDay(Today, id));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");
        Assert.Equal("servings (250 g each)", page.Find("option").TextContent);

        page.WaitForAssertion(() =>
        {
            page.Find("select").Change("Grams");
            Assert.Equal("250", page.Find("input[type=number]").GetAttribute("value"));
        });
        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("125");
            Assert.StartsWith("125 g · 0.5 servings: 153 kcal", Squeezed(page.Find(".amount-preview").TextContent));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        var day = await s.Data.Diary.GetDayAsync(Today, Token);
        Assert.Equal([("Porridge", "125 g", 152.5)], day.Entries.Select(e => (e.Name, e.Amount, e.Nutrients[Nutrients.Energy].Amount)));
        Assert.Equal(new LoggedAmount(null, id, 125, null, RecipeUnit.Grams), day.Entries[0].Logged);
    }

    [Fact]
    public async Task Switching_back_to_servings_converts_the_grams()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        Navigation.NavigateTo(DiaryUrls.AddAgain(Today, new LoggedAmount(null, id, 100, null, RecipeUnit.Grams)));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");

        page.WaitForAssertion(() =>
        {
            page.Find("select").Change("Servings");
            Assert.Equal("0.4", page.Find("input[type=number]").GetAttribute("value"));
        });
    }

    [Fact]
    public async Task An_empty_servings_field_disables_adding()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        Navigation.NavigateTo(RecipeUrls.AddToDay(Today, id));
        var page = Render<AddPage>();
        page.WaitForElement("form.amount-form");

        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("0");
            Assert.Equal("Enter an amount above 0.", page.Find(".amount-preview").TextContent);
        });
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_recipe_entry_is_edited_with_its_logged_nutrients()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        var entryId = await s.Recipes.LogAsync(Today, DiaryData.Noon, id, RecipeAmount.InServings(1), Token);
        await s.Recipes.UpdateAsync(id, Porridge(s) with { Servings = 1 }, Token);
        var page = Render<EntryPage>(p => p.Add(e => e.EntryId, entryId));
        page.WaitForElement("form.amount-form");

        Assert.StartsWith("1 serving · 250 g: 305 kcal", Squeezed(page.Find(".amount-preview").TextContent));
        Assert.Empty(page.FindAll("input[type=date]"));
        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("2");
            // Blazor's script in the browser sends a time input's value with seconds.
            page.Find("input[type=time]").Change("18:40:00");
            Assert.Equal(("2", "18:40:00"), (page.Find("input[type=number]").GetAttribute("value"), page.Find("input[type=time]").GetAttribute("value")));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        var entry = await s.Recipes.GetEntryAsync(entryId, Token);
        Assert.Equal((new TimeOnly(18, 40), RecipeAmount.InServings(2)), (entry!.Time, entry.Amount));
    }

    [Fact]
    public async Task A_recipe_entry_in_grams_is_edited_in_grams()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        var entryId = await s.Recipes.LogAsync(Today, DiaryData.Noon, id, RecipeAmount.InGrams(125), Token);
        var page = Render<EntryPage>(p => p.Add(e => e.EntryId, entryId));
        page.WaitForElement("form.amount-form");

        Assert.Equal(("125", "Grams"), (page.Find("input[type=number]").GetAttribute("value"), page.Find("select").GetAttribute("value")));
        page.WaitForAssertion(() =>
        {
            page.Find("input[type=number]").Change("200");
            Assert.Equal("200", page.Find("input[type=number]").GetAttribute("value"));
        });
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        Assert.Equal(RecipeAmount.InGrams(200), (await s.Recipes.GetEntryAsync(entryId, Token))!.Amount);
    }

    [Fact]
    public async Task A_recipe_entry_is_deleted()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        var entryId = await s.Recipes.LogAsync(Today, DiaryData.Noon, id, RecipeAmount.InServings(1), Token);
        var page = Render<EntryPage>(p => p.Add(e => e.EntryId, entryId));
        page.WaitForElement("button.danger");

        page.Find("button.danger").Click();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Day(Today), Navigation.Uri));
        Assert.Null(await s.Recipes.GetEntryAsync(entryId, Token));
    }
}
