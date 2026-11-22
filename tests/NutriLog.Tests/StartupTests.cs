using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NutriLog.Tests;

public sealed class StartupTests : IDisposable
{
    readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(b => b.UseEnvironment("Production"));

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task App_starts_and_serves_the_home_page()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<h1>NutriLog</h1>", html);
        Assert.Contains("_framework/blazor.web", html);
    }

    [Fact]
    public async Task Unknown_path_shows_the_not_found_page()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/no-such-page", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Error_page_shows_the_request_id()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Error", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("An error occurred while processing your request.", html);
        Assert.Matches("<strong>Request ID:</strong> <code>[^<]+</code>", html);
    }

    [Fact]
    public async Task Https_responses_carry_hsts_outside_development()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // UseHsts skips localhost.
            BaseAddress = new Uri("https://nutrilog.example"),
        });

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }
}
