using System.Globalization;
using System.Net;
using System.Text.Json;
using NutriLog.Data;

namespace NutriLog.Features.Barcode;

public sealed record OffPortion(string Name, double Grams);

public sealed record OffProduct(string Code, string Name, FoodBasis Basis, IReadOnlyDictionary<string, double> Per100, OffPortion? Serving);

public interface IOpenFoodFacts
{
    // Null when Open Food Facts doesn't know the barcode.
    Task<OffProduct?> GetProductAsync(string code, CancellationToken cancellationToken = default);
}

public sealed class OpenFoodFacts(HttpClient http) : IOpenFoodFacts
{
    public const string DefaultBaseUrl = "https://world.openfoodfacts.org/";
    // Open Food Facts asks for a User-Agent naming the app; a contact address is optional and stays out.
    public const string UserAgent = "NutriLog/1.0 (self-hosted food diary)";

    const string Fields = "code,product_name,product_name_en,product_name_nl,brands,nutriments,serving_size,serving_quantity,product_quantity_unit";

    public async Task<OffProduct?> GetProductAsync(string code, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/v2/product/{Uri.EscapeDataString(code)}?fields={Fields}", cancellationToken);
        // API v2 answers an unknown barcode with 404 and a status of 0 in the body.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        return OffMapping.Product(code, json.RootElement);
    }
}

public static class OffMapping
{
    const double MilliPerGram = 1_000;
    const double MicroPerGram = 1_000_000;
    const double KcalPerKj = 1 / 4.184;

    // Open Food Facts states every nutrient but energy in grams per 100 g (or ml); ours are in the unit of Nutrients.All.
    // Alcohol is left out: Open Food Facts gives it in % vol, not grams.
    static readonly (string Key, string NutrientId, double Factor)[] Map =
    [
        ("proteins", Nutrients.Protein, 1),
        ("fat", Nutrients.Fat, 1),
        ("carbohydrates", Nutrients.Carbohydrate, 1),
        ("fiber", Nutrients.Fibre, 1),
        ("sugars", Nutrients.Sugars, 1),
        ("saturated-fat", "saturated_fat", 1),
        ("monounsaturated-fat", "monounsaturated_fat", 1),
        ("polyunsaturated-fat", "polyunsaturated_fat", 1),
        ("trans-fat", "trans_fat", 1),
        ("cholesterol", "cholesterol", MilliPerGram),
        ("vitamin-a", "vitamin_a", MicroPerGram),
        ("vitamin-d", "vitamin_d", MicroPerGram),
        ("vitamin-e", "vitamin_e", MilliPerGram),
        ("vitamin-k", "vitamin_k", MicroPerGram),
        ("vitamin-b1", "vitamin_b1", MilliPerGram),
        ("vitamin-b2", "vitamin_b2", MilliPerGram),
        ("vitamin-pp", "vitamin_b3", MilliPerGram),
        ("vitamin-b6", "vitamin_b6", MilliPerGram),
        ("vitamin-b12", "vitamin_b12", MicroPerGram),
        ("vitamin-b9", "folate", MicroPerGram),
        ("vitamin-c", "vitamin_c", MilliPerGram),
        ("sodium", "sodium", MilliPerGram),
        ("potassium", "potassium", MilliPerGram),
        ("calcium", "calcium", MilliPerGram),
        ("magnesium", "magnesium", MilliPerGram),
        ("phosphorus", "phosphorus", MilliPerGram),
        ("iron", "iron", MilliPerGram),
        ("zinc", "zinc", MilliPerGram),
        ("copper", "copper", MilliPerGram),
        ("selenium", "selenium", MicroPerGram),
        ("iodine", "iodine", MicroPerGram),
    ];

    public static OffProduct? Product(string code, JsonElement root)
    {
        if (!root.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.Object)
            return null;
        if (root.TryGetProperty("status", out var status) && Number(status) is 0)
            return null;

        var basis = Text(product, "product_quantity_unit") is { } unit && unit.Equals("ml", StringComparison.OrdinalIgnoreCase)
            ? FoodBasis.Millilitres : FoodBasis.Grams;
        var per100 = product.TryGetProperty("nutriments", out var nutriments) && nutriments.ValueKind == JsonValueKind.Object
            ? Per100(nutriments) : [];
        return new OffProduct(code, Name(product, code), basis, per100, Serving(product));
    }

    public static Dictionary<string, double> Per100(JsonElement nutriments)
    {
        var per100 = new Dictionary<string, double>();
        if (Value(nutriments, "energy-kcal") is { } kcal)
            per100[Nutrients.Energy] = kcal;
        else if ((Value(nutriments, "energy-kj") ?? Value(nutriments, "energy")) is { } kj)
            per100[Nutrients.Energy] = kj * KcalPerKj;
        foreach (var (key, id, factor) in Map)
        {
            if (Value(nutriments, key) is { } amount)
                per100[id] = amount * factor;
        }
        // Labels state salt; Open Food Facts usually fills sodium from it, but not always.
        if (!per100.ContainsKey("sodium") && Value(nutriments, "salt") is { } salt)
            per100["sodium"] = salt * 400;
        return per100;
    }

    static string Name(JsonElement product, string code)
    {
        var name = Text(product, "product_name") ?? Text(product, "product_name_en") ?? Text(product, "product_name_nl");
        var brand = Text(product, "brands")?.Split(',')[0].Trim() is { Length: > 0 } first ? first : null;
        var full = (name, brand) switch
        {
            (null, null) => $"Product {code}",
            (null, _) => $"{brand} {code}",
            (_, null) => name,
            _ when name.Contains(brand, StringComparison.OrdinalIgnoreCase) => name,
            _ => $"{name} ({brand})"
        };
        return full.Length > AppDbContext.MaxNameLength ? full[..AppDbContext.MaxNameLength] : full;
    }

    static OffPortion? Serving(JsonElement product)
    {
        if (!product.TryGetProperty("serving_quantity", out var quantity) || Number(quantity) is not (> 0 and var grams))
            return null;
        var name = Text(product, "serving_size");
        if (name is null || name.Length > AppDbContext.MaxPortionNameLength)
            name = "serving";
        return new OffPortion(name, grams);
    }

    // Only the per-100 value counts; the bare key can be per serving.
    static double? Value(JsonElement nutriments, string key) =>
        nutriments.TryGetProperty(key + "_100g", out var value) && Number(value) is (>= 0 and var amount) && double.IsFinite(amount)
            ? amount : null;

    // Open Food Facts writes numbers as JSON numbers or as strings, depending on the product.
    static double? Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
        _ => null
    };

    static string? Text(JsonElement product, string key) =>
        product.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString()?.Trim() is { Length: > 0 } text ? text : null;
}
