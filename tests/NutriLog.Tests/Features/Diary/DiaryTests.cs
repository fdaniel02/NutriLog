using Microsoft.EntityFrameworkCore;
using NutriLog.Data;
using NutriLog.Features.Diary;
using NutriLog.Tests.Data;

namespace NutriLog.Tests.Features.Diary;

[Collection("Postgres")]
public class DiaryTests(PostgresFixture postgres)
{
    static readonly DateOnly Today = DiaryData.Today;
    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_day_lists_its_entries_with_totals()
    {
        var data = await DiaryData.CreateAsync(postgres);
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3), ("slice", 35));
        var cheese = await data.AddFoodAsync("Cheese", new Dictionary<string, double> { [Nutrients.Energy] = 400, [Nutrients.Protein] = 25 });
        await data.Diary.AddEntryAsync(Today, new TimeOnly(7, 30), bread.Id, new EntryAmount(2, bread.Portions[0].Id), Token);
        await data.Diary.AddEntryAsync(Today, new TimeOnly(8, 0), cheese.Id, new EntryAmount(30, null), Token);
        await data.Diary.AddEntryAsync(Today, new TimeOnly(19, 0), bread.Id, new EntryAmount(100, null), Token);
        await data.Diary.AddEntryAsync(Today.AddDays(1), DiaryData.Noon, bread.Id, new EntryAmount(100, null), Token);

        var day = await data.Diary.GetDayAsync(Today, Token);

        Assert.Equal([("Bread", "2 × slice · 70 g"), ("Cheese", "30 g"), ("Bread", "100 g")], day.Entries.Select(e => (e.Name, e.Amount)));
        Assert.Equal([175.0, 120.0, 250.0],day.Entries.Select(e => Math.Round(e.Nutrients[Nutrients.Energy].Amount, 6)));
        Assert.Equal(175 + 120 + 250, day.Totals[Nutrients.Energy].Amount, 6);
        Assert.True(day.Totals[Nutrients.Energy].Complete);
        // The cheese has no carbohydrate value.
        Assert.Equal(new NutrientTotal(31.5 + 45, 2, 1), day.Totals[Nutrients.Carbohydrate]);
    }

    [Fact]
    public async Task A_day_lists_its_entries_by_time_eaten_then_by_when_they_were_logged()
    {
        var data = await DiaryData.CreateAsync(postgres);
        var bread = await data.AddFoodAsync("Bread", []);
        var cheese = await data.AddFoodAsync("Cheese", []);
        var apple = await data.AddFoodAsync("Apple", []);
        await data.Diary.AddEntryAsync(Today, new TimeOnly(9, 0), bread.Id, new EntryAmount(100, null), Token);
        data.Time.Advance(TimeSpan.FromMinutes(1));
        await data.Diary.AddEntryAsync(Today, new TimeOnly(7, 30), cheese.Id, new EntryAmount(30, null), Token);
        data.Time.Advance(TimeSpan.FromMinutes(1));
        await data.Diary.AddEntryAsync(Today, new TimeOnly(9, 0), apple.Id, new EntryAmount(150, null), Token);

        var entries = (await data.Diary.GetDayAsync(Today, Token)).Entries;

        Assert.Equal([("Cheese", new TimeOnly(7, 30)), ("Bread", new TimeOnly(9, 0)), ("Apple", new TimeOnly(9, 0))],
            entries.Select(e => (e.Name, e.Time)));
    }

    [Fact]
    public async Task Search_finds_every_word_in_either_name_and_lists_used_foods_first()
    {
        var data = await DiaryData.CreateAsync(postgres);
        await data.AddFoodAsync("Whole milk", []);
        var semi = await data.AddFoodAsync("Semi-skimmed milk", []);
        await data.AddFoodAsync("Milk chocolate", DiaryData.Macros(530, 7, 57, 30));
        await data.AddFoodAsync("Oat drink", []);
        await data.Diary.AddEntryAsync(Today.AddDays(-3), DiaryData.Noon, semi.Id, new EntryAmount(200, null), Token);

        var milk = await data.Diary.SearchFoodsAsync("MILK", Token);
        var twoWords = await data.Diary.SearchFoodsAsync("milk whole", Token);

        Assert.Equal(["Semi-skimmed milk", "Milk chocolate", "Whole milk"], milk.Select(f => f.Name));
        Assert.Equal([true, false, false], milk.Select(f => f.Recent));
        Assert.Equal(530, milk[1].EnergyPer100);
        Assert.Equal(["Whole milk"], twoWords.Select(f => f.Name));
    }

    [Fact]
    public async Task Search_matches_the_dutch_name_and_treats_wildcards_as_text()
    {
        var data = await DiaryData.CreateAsync(postgres);
        await using (var db = await data.Factory.CreateDbContextAsync(Token))
        {
            db.Foods.AddRange(
                new Food { Name = "Cheese, Gouda 48+", NameNl = "Kaas Goudse 48+", Source = FoodSource.Own, Basis = FoodBasis.Grams },
                new Food { Name = "Fat 100%", Source = FoodSource.Own, Basis = FoodBasis.Grams },
                new Food { Name = "Fat 1000", Source = FoodSource.Own, Basis = FoodBasis.Grams });
            await db.SaveChangesAsync(Token);
        }

        Assert.Equal(["Cheese, Gouda 48+"], (await data.Diary.SearchFoodsAsync("goudse", Token)).Select(f => f.Name));
        Assert.Equal(["Fat 100%"], (await data.Diary.SearchFoodsAsync("100%", Token)).Select(f => f.Name));
    }

    [Fact]
    public async Task An_empty_search_lists_recent_foods_by_use()
    {
        var data = await DiaryData.CreateAsync(postgres);
        var once = await data.AddFoodAsync("Apple", []);
        var twice = await data.AddFoodAsync("Banana", []);
        var old = await data.AddFoodAsync("Kiwi", []);
        await data.AddFoodAsync("Pear", []);
        await data.Diary.AddEntryAsync(Today, DiaryData.Noon, once.Id, new EntryAmount(100, null), Token);
        await data.Diary.AddEntryAsync(Today, DiaryData.Noon, twice.Id, new EntryAmount(100, null), Token);
        await data.Diary.AddEntryAsync(Today.AddDays(-1), DiaryData.Noon, twice.Id, new EntryAmount(100, null), Token);
        await data.Diary.AddEntryAsync(Today.AddDays(-NutriLog.Features.Diary.Diary.RecentDays - 1), DiaryData.Noon, old.Id, new EntryAmount(100, null), Token);

        var recent = await data.Diary.SearchFoodsAsync("  ", Token);

        Assert.Equal(["Banana", "Apple"], recent.Select(f => f.Name));
    }

    [Fact]
    public async Task A_food_comes_with_its_portions_and_last_amount()
    {
        var data = await DiaryData.CreateAsync(postgres);
        var bread = await data.AddFoodAsync("Bread", DiaryData.Macros(250, 9, 45, 3), ("slice", 35), ("roll", 60));
        var slice = bread.Portions.Single(p => p.Name == "slice").Id;

        var unused = await data.Diary.GetFoodAsync(bread.Id, Token);
        await data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(80, null), Token);
        data.Time.Advance(TimeSpan.FromMinutes(1));
        await data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(3, slice), Token);
        var used = await data.Diary.GetFoodAsync(bread.Id, Token);

        Assert.Null(unused!.LastAmount);
        Assert.Equal(["slice", "roll"], unused.Portions.Select(p => p.Name));
        Assert.Equal(new EntryAmount(3, slice), used!.LastAmount);
        Assert.Null(await data.Diary.GetFoodAsync(-1, Token));
    }

    [Fact]
    public async Task Adding_rejects_a_bad_amount_or_another_foods_portion()
    {
        var data = await DiaryData.CreateAsync(postgres);
        var bread = await data.AddFoodAsync("Bread", [], ("slice", 35));
        var cheese = await data.AddFoodAsync("Cheese", [], ("slice", 20));

        await Assert.ThrowsAsync<ArgumentException>(() => data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(0, null), Token));
        await Assert.ThrowsAsync<ArgumentException>(() => data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(double.NaN, null), Token));
        await Assert.ThrowsAsync<ArgumentException>(() => data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(1, cheese.Portions[0].Id), Token));
        await Assert.ThrowsAsync<ArgumentException>(() => data.Diary.AddEntryAsync(Today, DiaryData.Noon, -1, new EntryAmount(1, null), Token));
    }

    [Fact]
    public async Task An_entry_can_be_changed_and_deleted()
    {
        var data = await DiaryData.CreateAsync(postgres);
        var bread = await data.AddFoodAsync("Bread", [], ("slice", 35));
        var id = await data.Diary.AddEntryAsync(Today, DiaryData.Noon, bread.Id, new EntryAmount(2, bread.Portions[0].Id), Token);

        var before = await data.Diary.GetEntryAsync(id, Token);
        Assert.True(await data.Diary.UpdateEntryAsync(id, new TimeOnly(19, 45), new EntryAmount(50, null), Token));
        var after = await data.Diary.GetEntryAsync(id, Token);
        Assert.True(await data.Diary.DeleteEntryAsync(id, Token));

        Assert.Equal((Today, DiaryData.Noon, new EntryAmount(2, bread.Portions[0].Id), "Bread"),
            (before!.Date, before.Time, before.Amount, before.Food.Name));
        Assert.Equal((new TimeOnly(19, 45), new EntryAmount(50, null)), (after!.Time, after.Amount));
        Assert.Null(await data.Diary.GetEntryAsync(id, Token));
        Assert.False(await data.Diary.UpdateEntryAsync(id, DiaryData.Noon, new EntryAmount(50, null), Token));
        Assert.False(await data.Diary.DeleteEntryAsync(id, Token));
    }

    [Fact]
    public async Task Copies_a_whole_day_with_its_times()
    {
        var data = await DiaryData.CreateAsync(postgres);
        var bread = await data.AddFoodAsync("Bread", [], ("slice", 35));
        var yesterday = Today.AddDays(-1);
        await data.Diary.AddEntryAsync(yesterday, new TimeOnly(12, 45), bread.Id, new EntryAmount(80, null), Token);
        await data.Diary.AddEntryAsync(yesterday, new TimeOnly(7, 30), bread.Id, new EntryAmount(2, bread.Portions[0].Id), Token);
        await data.Diary.AddEntryAsync(Today, new TimeOnly(10, 0), bread.Id, new EntryAmount(20, null), Token);

        var copied = await data.Diary.CopyAsync(yesterday, Today, Token);

        Assert.Equal(2, copied);
        Assert.Equal([("2 × slice · 70 g", new TimeOnly(7, 30)), ("20 g", new TimeOnly(10, 0)), ("80 g", new TimeOnly(12, 45))],
            (await data.Diary.GetDayAsync(Today, Token)).Entries.Select(e => (e.Amount, e.Time)));
        Assert.Equal(2, (await data.Diary.GetDayAsync(yesterday, Token)).Entries.Count);
        Assert.Equal(0, await data.Diary.CopyAsync(Today.AddDays(-5), Today, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => data.Diary.CopyAsync(Today, Today, Token));
    }
}
