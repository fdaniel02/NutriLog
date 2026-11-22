using Microsoft.Extensions.Time.Testing;
using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Tests.Features.Diary;

public class DiaryUrlsTests
{
    static readonly DateOnly Today = new(2026, 11, 22);

    [Theory]
    [InlineData(2026, 11, 22, "Today")]
    [InlineData(2026, 11, 21, "Yesterday")]
    [InlineData(2026, 11, 23, "Tomorrow")]
    [InlineData(2026, 11, 18, "Wed 18 Nov")]
    [InlineData(2025, 12, 31, "Wed 31 Dec 2025")]
    public void Names_the_day(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, DiaryUrls.DayTitle(new DateOnly(year, month, day), Today));
    }

    [Fact]
    public void Builds_the_urls()
    {
        Assert.Equal(
            ["?date=2026-11-22", "add?date=2026-11-22&meal=Lunch", "add?date=2026-11-22&meal=Lunch&food=7", "entry/3", "foods/new?date=2026-11-22&meal=Snack"],
            [DiaryUrls.Day(Today), DiaryUrls.Add(Today, Meal.Lunch), DiaryUrls.Add(Today, Meal.Lunch, 7), DiaryUrls.Entry(3), DiaryUrls.NewFood(Today, Meal.Snack)]);
    }

    [Theory]
    [InlineData("2026-11-22", true)]
    [InlineData("22-11-2026", false)]
    [InlineData(null, false)]
    public void Parses_only_iso_dates(string? text, bool valid)
    {
        Assert.Equal(valid ? Today : null, DiaryUrls.ParseDate(text));
    }

    [Theory]
    [InlineData("lunch", Meal.Lunch)]
    [InlineData("Snack", Meal.Snack)]
    [InlineData("7", null)]
    [InlineData("brunch", null)]
    public void Parses_meals(string text, Meal? expected)
    {
        Assert.Equal(expected, DiaryUrls.ParseMeal(text));
    }

    [Fact]
    public void Today_is_in_the_configured_time_zone()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 23, 30, 0, TimeSpan.Zero));

        var clock = new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"), time);

        Assert.Equal(new DateOnly(2026, 11, 23), clock.Today);
    }
}
