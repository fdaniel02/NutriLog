using System.Net;
using System.Text;

namespace NutriLog.Tests.Features.Barcode;

// Responses in API v2's format, with a made-up product.
public static class OffSamples
{
    public const string Code = "8712345678906";

    public const string Product = """
        {
          "code": "8712345678906",
          "status": 1,
          "status_verbose": "product found",
          "product": {
            "product_name": "Oat crackers",
            "brands": "Brandname, Other brand",
            "serving_size": "3 crackers (25 g)",
            "serving_quantity": "25",
            "product_quantity_unit": "g",
            "nutriments": {
              "energy-kcal_100g": 420,
              "energy-kj_100g": 1760,
              "energy-kcal_serving": 105,
              "proteins_100g": 10.5,
              "fat_100g": "12",
              "saturated-fat_100g": 1.5,
              "carbohydrates_100g": 64,
              "sugars_100g": 2.1,
              "fiber_100g": 8,
              "sodium_100g": 0.48,
              "salt_100g": 1.2,
              "calcium_100g": 0.05,
              "vitamin-b12_100g": 0.0000015,
              "alcohol_100g": 0,
              "iron_100g": -1
            }
          }
        }
        """;

    public const string NotFound = """{"code":"8712345678906","status":0,"status_verbose":"product not found"}""";

    public sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
