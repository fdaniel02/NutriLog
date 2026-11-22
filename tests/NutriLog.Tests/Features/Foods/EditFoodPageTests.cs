using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NutriLog.Data;
using NutriLog.Features.Barcode;
using NutriLog.Features.Diary;
using NutriLog.Features.Foods;
using NutriLog.Tests.Data;
using NutriLog.Tests.Features.Diary;

namespace NutriLog.Tests.Features.Foods;

[Collection("Postgres")]
public class EditFoodPageTests(PostgresFixture postgres) : BunitContext
{
    static readonly DateOnly Today = DiaryData.Today;
    static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    async Task<DiaryData> SetUpAsync(string url = "")
    {
        var data = await DiaryData.CreateAsync(postgres);
        Services.AddSingleton(data.Clock);
        Services.AddSingleton<IDiary>(data.Diary);
        Services.AddSingleton<IFoods>(new NutriLog.Features.Foods.Foods(data.Factory, data.Time));
        Navigation.NavigateTo(url);
        return data;
    }

    NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    static async Task<int> AddScannedFoodAsync(DiaryData data)
    {
        await using var db = await data.Factory.CreateDbContextAsync(Token);
        var food = new Food { Name = "Oat crackers", Source = FoodSource.Off, SourceId = "87123456", Barcode = "87123456" };
        food.Nutrients.Add(new FoodNutrient { NutrientId = Nutrients.Energy, Amount = 420 });
        food.Nutrients.Add(new FoodNutrient { NutrientId = "sodium", Amount = 480 });
        food.Portions.Add(new Portion { Name = "3 crackers", Grams = 25, Imported = true });
        db.Foods.Add(food);
        await db.SaveChangesAsync(Token);
        return food.Id;
    }

    [Fact]
    public async Task Corrects_a_scanned_product()
    {
        var data = await SetUpAsync();
        var id = await AddScannedFoodAsync(data);
        Navigation.NavigateTo(FoodUrls.Edit(id, Today, Meal.Lunch));
        var page = Render<NewFoodPage>(p => p.Add(x => x.FoodId, id));
        page.WaitForElement("form.food-form");

        Assert.Equal("Edit food", page.Find("h1").TextContent);
        Assert.Equal("Barcode 87123456 · from Open Food Facts", page.Find(".food-barcode").TextContent);
        Assert.Equal("3 crackers · 25 g", page.Find(".existing-portion").TextContent);
        var salt = page.FindAll("label").Single(l => l.TextContent.Trim().StartsWith("Salt")).QuerySelector("input")!;
        Assert.Equal("1.2", salt.GetAttribute("value"));
        page.FindAll("input[type=number]")[0].Change("400");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Add(Today, Meal.Lunch, id), Navigation.Uri));
        await using var db = await data.Factory.CreateDbContextAsync(Token);
        var food = await db.Foods.Include(f => f.Nutrients).SingleAsync(f => f.Id == id, Token);
        Assert.Equal([(Nutrients.Energy, 400.0), ("sodium", 480.0)],
            food.Nutrients.OrderBy(n => n.NutrientId).Select(n => (n.NutrientId, n.Amount)));
    }

    [Fact]
    public async Task An_imported_food_cant_be_edited()
    {
        var data = await SetUpAsync();
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));

        var page = Render<NewFoodPage>(p => p.Add(x => x.FoodId, bread.Id));

        page.WaitForAssertion(() => Assert.StartsWith("This food can't be edited", page.Find("p.muted").TextContent));
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public async Task A_new_food_from_an_unknown_barcode_keeps_the_barcode()
    {
        var data = await SetUpAsync(FoodUrls.NewWithBarcode(Today, Meal.Snack, "87123456"));
        var page = Render<NewFoodPage>();
        page.WaitForElement("form.food-form");
        Assert.Equal("Barcode 87123456", page.Find(".food-barcode").TextContent);

        page.Find("input[type=text]").Change("Crackers");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("/add?", Navigation.Uri));
        await using var db = await data.Factory.CreateDbContextAsync(Token);
        Assert.True(await db.Foods.AnyAsync(f => f.Barcode == "87123456" && f.Source == FoodSource.Own, Token));
    }

    [Fact]
    public async Task The_add_page_links_to_scanning_and_to_editing_an_own_or_scanned_food()
    {
        var data = await SetUpAsync(DiaryUrls.Add(Today, Meal.Lunch));
        var id = await AddScannedFoodAsync(data);
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3));
        var page = Render<AddPage>();
        page.WaitForElement(".search");
        Assert.Contains(BarcodeUrls.Scan(Today, Meal.Lunch), page.FindAll(".add-link").Select(a => a.GetAttribute("href")));

        Navigation.NavigateTo(DiaryUrls.Add(Today, Meal.Lunch, id));
        page.WaitForAssertion(() =>
            Assert.Contains(FoodUrls.Edit(id, Today, Meal.Lunch), page.FindAll("a.button").Select(a => a.GetAttribute("href"))));

        Navigation.NavigateTo(DiaryUrls.Add(Today, Meal.Lunch, bread.Id));
        page.WaitForAssertion(() => Assert.Equal("Bread", page.Find("form.amount-form h2").TextContent));
        Assert.DoesNotContain(page.FindAll("a.button"), a => a.TextContent == "Edit food");
    }
}
