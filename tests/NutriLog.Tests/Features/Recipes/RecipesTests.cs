using Microsoft.EntityFrameworkCore;
using NutriLog.Data;
using NutriLog.Features.Diary;
using NutriLog.Features.Recipes;
using NutriLog.Tests.Data;
using NutriLog.Tests.Features.Diary;

namespace NutriLog.Tests.Features.Recipes;

[Collection("Postgres")]
public class RecipesTests(PostgresFixture postgres)
{
    static readonly DateOnly Today = DiaryData.Today;
    static CancellationToken Token => TestContext.Current.CancellationToken;

    sealed record Setup(DiaryData Data, NutriLog.Features.Recipes.Recipes Recipes, Food Oats, Food Milk);

    async Task<Setup> SetUpAsync()
    {
        var data = await DiaryData.CreateAsync(postgres);
        var oats = await data.AddFoodAsync("Oats", DiaryData.Macros(370, 13, 60, 7));
        var milk = await data.AddFoodAsync("Milk", new Dictionary<string, double> { [Nutrients.Energy] = 60, [Nutrients.Protein] = 3.4 });
        return new Setup(data, new NutriLog.Features.Recipes.Recipes(data.Factory, data.Time), oats, milk);
    }

    static RecipeInput Porridge(Setup s, double servings = 2, double oats = 100, double milk = 400) =>
        new("Porridge", servings, [new(s.Oats.Id, oats), new(s.Milk.Id, milk)]);

    [Fact]
    public async Task A_recipe_has_its_nutrients_per_serving()
    {
        var s = await SetUpAsync();

        var id = await s.Recipes.CreateAsync(Porridge(s) with { Name = "  Porridge " }, Token);
        var recipe = await s.Recipes.GetAsync(id, Token);

        Assert.Equal(("Porridge", 2.0), (recipe!.Name, recipe.Servings));
        Assert.Equal(["Milk", "Oats"], recipe.Ingredients.Select(i => i.Name));
        Assert.Equal(new NutrientTotal((370 + 240) / 2.0, 1, 0), recipe.PerServing[Nutrients.Energy]);
        // The milk has no carbohydrate value, so the carbohydrate is at least the oats'.
        Assert.Equal(new NutrientTotal(30, 1, 1), recipe.PerServing[Nutrients.Carbohydrate]);
        Assert.True(recipe.PerServing["sodium"].Unknown);
    }

    [Fact]
    public async Task A_food_listed_twice_is_one_ingredient_with_both_amounts()
    {
        var s = await SetUpAsync();

        var id = await s.Recipes.CreateAsync(new RecipeInput("Oats", 1, [new(s.Oats.Id, 40), new(s.Oats.Id, 60)]), Token);

        Assert.Equal(100, (await s.Recipes.GetAsync(id, Token))!.Ingredients.Single().Grams);
    }

    [Theory]
    [InlineData("", 1, 100, "The name must be")]
    [InlineData("Porridge", 0, 100, "The number of servings")]
    [InlineData("Porridge", double.NaN, 100, "The number of servings")]
    [InlineData("Porridge", 1, 0, "Each food needs")]
    public async Task An_invalid_recipe_is_refused(string name, double servings, double grams, string message)
    {
        var s = await SetUpAsync();

        var e = await Assert.ThrowsAsync<InvalidRecipeException>(() =>
            s.Recipes.CreateAsync(new RecipeInput(name, servings, [new(s.Oats.Id, grams)]), Token));

        Assert.StartsWith(message, e.Message);
    }

    [Fact]
    public async Task A_recipe_needs_foods_that_exist()
    {
        var s = await SetUpAsync();

        Assert.Equal("Add at least one food.", (await Assert.ThrowsAsync<InvalidRecipeException>(() =>
            s.Recipes.CreateAsync(new RecipeInput("Empty", 1, []), Token))).Message);
        Assert.Equal("One of the foods doesn't exist.", (await Assert.ThrowsAsync<InvalidRecipeException>(() =>
            s.Recipes.CreateAsync(new RecipeInput("Ghost", 1, [new(-1, 10)]), Token))).Message);
    }

    [Fact]
    public async Task An_update_replaces_name_servings_and_foods()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);

        Assert.True(await s.Recipes.UpdateAsync(id, new RecipeInput("Oat bowl", 1, [new(s.Oats.Id, 80)]), Token));

        var recipe = await s.Recipes.GetAsync(id, Token);
        Assert.Equal(("Oat bowl", 1.0), (recipe!.Name, recipe.Servings));
        Assert.Equal([(s.Oats.Id, 80.0)], recipe.Ingredients.Select(i => (i.FoodId, i.Grams)));
        Assert.False(await s.Recipes.UpdateAsync(-1, Porridge(s), Token));
    }

    [Fact]
    public async Task Search_matches_every_word_and_an_empty_query_lists_all_by_name()
    {
        var s = await SetUpAsync();
        await s.Recipes.CreateAsync(Porridge(s), Token);
        await s.Recipes.CreateAsync(Porridge(s) with { Name = "Overnight oats" }, Token);
        await s.Recipes.CreateAsync(Porridge(s) with { Name = "100% oats" }, Token);

        Assert.Equal(["100% oats", "Overnight oats", "Porridge"], (await s.Recipes.SearchAsync("", Token)).Select(r => r.Name));
        Assert.Equal(["Overnight oats"], (await s.Recipes.SearchAsync("OATS night", Token)).Select(r => r.Name));
        Assert.Equal(["100% oats"], (await s.Recipes.SearchAsync("0%", Token)).Select(r => r.Name));
        var porridge = (await s.Recipes.SearchAsync("porr", Token)).Single();
        Assert.Equal((2.0, 305.0), (porridge.Servings, porridge.EnergyPerServing.Amount));
    }

    [Fact]
    public async Task A_logged_recipe_keeps_its_nutrients_when_the_recipe_changes()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        await s.Recipes.LogAsync(Today, Meal.Breakfast, id, 1.5, Token);

        await s.Recipes.UpdateAsync(id, Porridge(s, oats: 200), Token);
        var later = await s.Recipes.LogAsync(Today, Meal.Snack, id, 1, Token);

        var day = await s.Data.Diary.GetDayAsync(Today, Token);
        var breakfast = day.Meals.Single(m => m.Meal == Meal.Breakfast).Entries.Single();
        Assert.Equal(("Porridge", "1.5 servings"), (breakfast.Name, breakfast.Amount));
        Assert.Equal(305 * 1.5, breakfast.Nutrients[Nutrients.Energy].Amount, 6);
        Assert.Equal(new NutrientTotal(30 * 1.5, 1, 1), breakfast.Nutrients[Nutrients.Carbohydrate]);
        Assert.Equal(new NutrientTotal(0, 0, 1), breakfast.Nutrients["sodium"]);
        var snack = day.Meals.Single(m => m.Meal == Meal.Snack).Entries.Single();
        Assert.Equal((later, "1 serving", 490.0), (snack.Id, snack.Amount, snack.Nutrients[Nutrients.Energy].Amount));
        Assert.Equal(305 * 1.5 + 490, day.Totals[Nutrients.Energy].Amount, 6);
    }

    [Fact]
    public async Task A_recipe_entry_can_be_read_changed_and_deleted()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        var entryId = await s.Recipes.LogAsync(Today, Meal.Breakfast, id, 1, Token);
        await s.Recipes.UpdateAsync(id, Porridge(s, oats: 200), Token);

        Assert.True(await s.Recipes.UpdateEntryAsync(entryId, Meal.Lunch, 2, Token));

        var entry = await s.Recipes.GetEntryAsync(entryId, Token);
        Assert.Equal((Today, Meal.Lunch, id, "Porridge", 2.0), (entry!.Date, entry.Meal, entry.RecipeId, entry.RecipeName, entry.Servings));
        // Still the recipe as it was logged.
        Assert.Equal(610, entry.Nutrients(entry.Servings)[Nutrients.Energy].Amount, 6);
        Assert.Null(await s.Data.Diary.GetEntryAsync(entryId, Token));
        Assert.False(await s.Recipes.UpdateEntryAsync(-1, Meal.Lunch, 2, Token));

        Assert.True(await s.Data.Diary.DeleteEntryAsync(entryId, Token));
        Assert.Null(await s.Recipes.GetEntryAsync(entryId, Token));
        await using var db = await s.Data.Factory.CreateDbContextAsync(Token);
        Assert.False(await db.DiaryEntryNutrients.AnyAsync(Token));
    }

    [Fact]
    public async Task A_food_entry_is_not_a_recipe_entry()
    {
        var s = await SetUpAsync();
        var entryId = await s.Data.Diary.AddEntryAsync(Today, Meal.Lunch, s.Oats.Id, new EntryAmount(50, null), Token);

        Assert.Null(await s.Recipes.GetEntryAsync(entryId, Token));
        Assert.False(await s.Recipes.UpdateEntryAsync(entryId, Meal.Lunch, 2, Token));
    }

    [Fact]
    public async Task Logging_needs_servings_above_zero_and_a_recipe_that_exists()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);

        await Assert.ThrowsAsync<ArgumentException>(() => s.Recipes.LogAsync(Today, Meal.Lunch, id, 0, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => s.Recipes.LogAsync(Today, Meal.Lunch, -1, 1, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => s.Recipes.UpdateEntryAsync(1, Meal.Lunch, double.PositiveInfinity, Token));
    }

    [Fact]
    public async Task A_copied_recipe_counts_as_the_recipe_is_now()
    {
        var s = await SetUpAsync();
        var id = await s.Recipes.CreateAsync(Porridge(s), Token);
        await s.Recipes.LogAsync(Today.AddDays(-1), Meal.Breakfast, id, 1, Token);
        await s.Data.Diary.AddEntryAsync(Today.AddDays(-1), Meal.Breakfast, s.Milk.Id, new EntryAmount(100, null), Token);
        await s.Recipes.UpdateAsync(id, Porridge(s, oats: 200), Token);

        Assert.Equal(2, await s.Data.Diary.CopyAsync(Today.AddDays(-1), Today, Meal.Breakfast, Token));

        var copied = (await s.Data.Diary.GetDayAsync(Today, Token)).Meals[0].Entries;
        Assert.Equal([("Porridge", 490.0), ("Milk", 60.0)], copied.Select(e => (e.Name, e.Nutrients[Nutrients.Energy].Amount)));
        var yesterday = (await s.Data.Diary.GetDayAsync(Today.AddDays(-1), Token)).Meals[0].Entries;
        Assert.Equal(305, yesterday[0].Nutrients[Nutrients.Energy].Amount, 6);
    }

    [Fact]
    public async Task A_recipe_in_the_diary_stays()
    {
        var s = await SetUpAsync();
        var logged = await s.Recipes.CreateAsync(Porridge(s), Token);
        var unused = await s.Recipes.CreateAsync(Porridge(s) with { Name = "Unused" }, Token);
        await s.Recipes.LogAsync(Today, Meal.Breakfast, logged, 1, Token);

        Assert.Equal(RecipeDeleteResult.InDiary, await s.Recipes.DeleteAsync(logged, Token));
        Assert.Equal(RecipeDeleteResult.Deleted, await s.Recipes.DeleteAsync(unused, Token));
        Assert.Equal(RecipeDeleteResult.NotFound, await s.Recipes.DeleteAsync(unused, Token));
        Assert.NotNull(await s.Recipes.GetAsync(logged, Token));
        Assert.Null(await s.Recipes.GetAsync(unused, Token));
    }

    [Fact]
    public void Servings_read_as_one_serving_or_several()
    {
        Assert.Equal(["1 serving", "0.5 servings", "2 servings"], new[] { 1, 0.5, 2 }.Select(NutrientFormat.Servings));
    }
}
