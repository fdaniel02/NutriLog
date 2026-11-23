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
            ["?date=2026-11-22", "add?date=2026-11-22", "add?date=2026-11-22&food=7", "entry/3", "foods/new?date=2026-11-22"],
            [DiaryUrls.Day(Today), DiaryUrls.Add(Today), DiaryUrls.Add(Today, 7), DiaryUrls.Entry(3), DiaryUrls.NewFood(Today)]);
    }

    [Fact]
    public void Builds_the_urls_to_add_an_entry_again()
    {
        Assert.Equal(
            ["add?date=2026-11-22&food=7&amount=2&portion=4", "add?date=2026-11-22&food=7&amount=12.5", "add?date=2026-11-22&recipe=3&servings=1.5"],
            [DiaryUrls.AddAgain(Today, new LoggedAmount(7, null, 2, 4)), DiaryUrls.AddAgain(Today, new LoggedAmount(7, null, 12.5, null)),
                DiaryUrls.AddAgain(Today, new LoggedAmount(null, 3, 1.5, null))]);
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
    [InlineData(7, 5, "07:05")]
    [InlineData(19, 30, "19:30")]
    public void Shows_the_time_of_day_in_24_hours(int hour, int minute, string expected)
    {
        Assert.Equal(expected, DiaryUrls.TimeOfDay(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void Today_is_in_the_configured_time_zone()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 23, 30, 0, TimeSpan.Zero));

        var clock = new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"), time);

        Assert.Equal(new DateOnly(2026, 11, 23), clock.Today);
    }

    [Theory]
    [InlineData(2026, 11, 22, 23, 30, 45, 0, 30)]
    [InlineData(2026, 7, 1, 6, 15, 59, 8, 15)]
    public void Now_is_the_time_of_day_in_the_configured_time_zone_in_whole_minutes(
        int year, int month, int day, int hourUtc, int minute, int second, int expectedHour, int expectedMinute)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(year, month, day, hourUtc, minute, second, TimeSpan.Zero));

        var clock = new Clock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"), time);

        Assert.Equal(new TimeOnly(expectedHour, expectedMinute), clock.Now);
    }
}
