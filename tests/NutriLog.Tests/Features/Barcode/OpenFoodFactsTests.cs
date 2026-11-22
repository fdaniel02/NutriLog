using System.Net;
using NutriLog.Features.Barcode;

namespace NutriLog.Tests.Features.Barcode;

public class OpenFoodFactsTests
{
    static (OpenFoodFacts Client, OffSamples.Handler Handler) Create(HttpStatusCode status, string json)
    {
        var handler = new OffSamples.Handler(_ => OffSamples.Json(status, json));
        return (new OpenFoodFacts(new HttpClient(handler) { BaseAddress = new Uri("https://off.test/") }), handler);
    }

    [Fact]
    public async Task Asks_api_v2_for_the_barcode_and_maps_the_product()
    {
        var (client, handler) = Create(HttpStatusCode.OK, OffSamples.Product);

        var product = await client.GetProductAsync(OffSamples.Code, TestContext.Current.CancellationToken);

        Assert.Equal("Oat crackers (Brandname)", product?.Name);
        var uri = handler.Requests.Single().RequestUri!;
        Assert.Equal("/api/v2/product/8712345678906", uri.AbsolutePath);
        Assert.Contains("fields=", uri.Query);
    }

    [Fact]
    public async Task An_unknown_barcode_is_null()
    {
        var (client, _) = Create(HttpStatusCode.NotFound, OffSamples.NotFound);

        Assert.Null(await client.GetProductAsync(OffSamples.Code, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_server_error_throws()
    {
        var (client, _) = Create(HttpStatusCode.ServiceUnavailable, "");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetProductAsync(OffSamples.Code, TestContext.Current.CancellationToken));
    }
}
