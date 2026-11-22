using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Testing;
using NutriLog.Features.Targets;

namespace NutriLog.Tests.Features.Targets;

public class RunStatsClientTests
{
    static readonly DateOnly Day = new(2026, 11, 22);
    static CancellationToken Token => TestContext.Current.CancellationToken;

    readonly FakeLogger<RunStatsClient> logger = new();

    RunStatsClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null) =>
        new(new HttpClient(new FakeHandler(respond))
        {
            BaseAddress = RunStatsClient.BaseAddress("http://runstats.test"),
            Timeout = timeout ?? RunStatsClient.Timeout
        }, logger);

    static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Sums_the_days_runs_from_api_runs()
    {
        Uri? asked = null;
        var client = Client((request, _) =>
        {
            asked = request.RequestUri;
            return Task.FromResult(Json("""
                [
                  {"id":1,"date":"2026-11-22","startUtc":"2026-11-22T07:00:00Z","name":"Morning run","typeKey":"running",
                   "distanceMeters":10200,"durationSeconds":3200,"calories":650,"trainingLoad":120.5,"averageHeartRate":150},
                  {"id":2,"date":"2026-11-22","startUtc":"2026-11-22T17:00:00Z","name":"Shakeout","typeKey":"running",
                   "distanceMeters":3000,"durationSeconds":1100,"calories":null,"trainingLoad":null,"averageHeartRate":null}
                ]
                """));
        });

        var day = await client.GetDayAsync(Day, Token);

        Assert.Equal(new Uri("http://runstats.test/api/runs?from=2026-11-22&to=2026-11-22"), asked);
        Assert.Equal(new RunDay(2, 13.2, 650, 1), day);
        Assert.Empty(logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task A_day_without_runs_is_an_empty_day()
    {
        var day = await Client((_, _) => Task.FromResult(Json("[]"))).GetDayAsync(Day, Token);

        Assert.Equal(RunDay.None, day);
    }

    [Fact]
    public async Task An_error_status_counts_as_unreachable_and_is_logged()
    {
        var day = await Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))).GetDayAsync(Day, Token);

        Assert.Null(day);
        var log = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(20, log.Id.Id);
        Assert.Contains("2026-11-22", log.Message);
        Assert.Contains("HttpRequestException", log.Message);
    }

    [Fact]
    public async Task A_refused_connection_counts_as_unreachable()
    {
        var day = await Client((_, _) => throw new HttpRequestException("Connection refused")).GetDayAsync(Day, Token);

        Assert.Null(day);
    }

    [Theory]
    [InlineData("not json", "application/json")]
    [InlineData("<html></html>", "text/html")]
    public async Task An_unexpected_answer_counts_as_unreachable(string body, string mediaType)
    {
        var day = await Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        })).GetDayAsync(Day, Token);

        Assert.Null(day);
    }

    [Fact]
    public async Task A_slow_answer_counts_as_unreachable()
    {
        var client = Client(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json("[]");
        }, TimeSpan.FromMilliseconds(50));

        Assert.Null(await client.GetDayAsync(Day, Token));
    }

    [Fact]
    public async Task The_callers_cancellation_is_passed_on()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var client = Client((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetDayAsync(Day, cancelled.Token));
    }

    [Theory]
    [InlineData("http://localhost:5310")]
    [InlineData("http://localhost:5310/")]
    public void The_base_address_ends_with_a_slash_so_paths_are_relative_to_it(string configured)
    {
        Assert.Equal(new Uri("http://localhost:5310/"), RunStatsClient.BaseAddress(configured));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void A_missing_base_address_is_an_error(string? configured)
    {
        var error = Assert.Throws<InvalidOperationException>(() => RunStatsClient.BaseAddress(configured));
        Assert.Contains("RunStats:BaseUrl", error.Message);
    }

    sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
