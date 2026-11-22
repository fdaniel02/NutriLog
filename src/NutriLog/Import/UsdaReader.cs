using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using NutriLog.Data;

namespace NutriLog.Import;

// Reads a FoodData Central JSON download (Foundation Foods or SR Legacy) as the zip USDA publishes. Values are per
// 100 g; FoodData Central has no per-100-ml foods.
public static partial class UsdaReader
{
    // FoodData Central nutrient ids, in order of preference where a nutrient has more than one.
    // Foundation Foods mostly has energy only from the Atwater factors (2048 specific, 2047 general), not 1008;
    // folate in dietary folate equivalents (1190) matches NEVO, total folate (1177) is the fallback.
    public static readonly IReadOnlyDictionary<string, int[]> NutrientIds = new Dictionary<string, int[]>
    {
        [Nutrients.Energy] = [1008, 2048, 2047],
        [Nutrients.Protein] = [1003],
        [Nutrients.Fat] = [1004],
        [Nutrients.Carbohydrate] = [1005],
        [Nutrients.Fibre] = [1079, 2033],
        [Nutrients.Sugars] = [2000, 1063],
        [Nutrients.Alcohol] = [1018],
        ["saturated_fat"] = [1258],
        ["monounsaturated_fat"] = [1292],
        ["polyunsaturated_fat"] = [1293],
        ["trans_fat"] = [1257],
        ["cholesterol"] = [1253],
        ["vitamin_a"] = [1106],
        ["vitamin_d"] = [1114],
        ["vitamin_e"] = [1109],
        ["vitamin_k"] = [1185],
        ["vitamin_b1"] = [1165],
        ["vitamin_b2"] = [1166],
        ["vitamin_b3"] = [1167],
        ["vitamin_b6"] = [1175],
        ["vitamin_b12"] = [1178],
        ["folate"] = [1190, 1177],
        ["vitamin_c"] = [1162],
        ["sodium"] = [1093],
        ["potassium"] = [1092],
        ["calcium"] = [1087],
        ["magnesium"] = [1090],
        ["phosphorus"] = [1091],
        ["iron"] = [1089],
        ["zinc"] = [1095],
        ["copper"] = [1098],
        ["selenium"] = [1103],
        ["iodine"] = [1100],
    };

    // Portions that measure an amount (volume, weight, servings, packaging) rather than count a thing; grams are
    // better for those. Matched against the portion name's first word.
    static readonly HashSet<string> Measures = new(StringComparer.OrdinalIgnoreCase)
    {
        "oz", "fl", "lb", "lbs", "pound", "cup", "cups", "tbsp", "tablespoon", "tablespoons", "tsp", "teaspoon",
        "teaspoons", "cubic", "serving", "servings", "nlea", "racc", "package", "packet", "can", "jar", "container",
        "bottle", "box", "bag", "envelope", "scoop", "quart", "pint", "gallon", "liter", "milliliter", "ml", "g",
        "gram", "grams", "kg",
    };

    // The release date in USDA's file name, e.g. FoodData_Central_foundation_food_json_2026-10-29.zip.
    [GeneratedRegex(@"_(?<date>\d{4}-\d{2}(-\d{2})?)\.(zip|json)$")]
    private static partial Regex ReleaseDate();

    public static ImportedDataset Read(Stream zip, string fileName)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var entry = archive.Entries.SingleOrDefault(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            ?? throw new ImportFormatException("The zip has no JSON file. Download the JSON version from FoodData Central.");
        using var json = entry.Open();
        using var document = JsonDocument.Parse(json);

        var (source, foods) = document.RootElement switch
        {
            var root when root.TryGetProperty("FoundationFoods", out var f) => (FoodSource.UsdaFoundation, f),
            var root when root.TryGetProperty("SRLegacyFoods", out var f) => (FoodSource.UsdaSrLegacy, f),
            _ => throw new ImportFormatException("The JSON holds neither Foundation Foods nor SR Legacy foods."),
        };
        var match = ReleaseDate().Match(fileName);
        if (!match.Success)
            throw new ImportFormatException($"Can't read the release date from the file name \"{fileName}\"; keep USDA's name.");

        return new ImportedDataset(source, match.Groups["date"].Value, foods.EnumerateArray().Select(Food).ToList());
    }

    static ImportedFood Food(JsonElement food)
    {
        var amounts = new Dictionary<int, double>();
        foreach (var nutrient in food.GetProperty("foodNutrients").EnumerateArray())
        {
            // Carbohydrate "by difference" (100 g minus water, protein, fat and ash) comes out slightly below 0 for some
            // meats; it means none.
            if (nutrient.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number)
                amounts.TryAdd(nutrient.GetProperty("nutrient").GetProperty("id").GetInt32(), Math.Max(0, amount.GetDouble()));
        }

        var nutrients = new Dictionary<string, double>();
        foreach (var (nutrientId, ids) in NutrientIds)
        {
            foreach (var id in ids)
            {
                if (amounts.TryGetValue(id, out var value))
                {
                    nutrients[nutrientId] = value;
                    break;
                }
            }
        }

        var portions = food.TryGetProperty("foodPortions", out var list)
            ? list.EnumerateArray().Select(Portion).OfType<ImportedPortion>().DistinctBy(p => p.Name).ToList()
            : [];

        return new ImportedFood(
            food.GetProperty("fdcId").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture),
            food.GetProperty("description").GetString()!,
            null,
            FoodBasis.Grams,
            nutrients,
            portions);
    }

    // SR Legacy puts the whole portion into the modifier ("medium (7\" to 7-7/8\" long)") with the unit
    // "undetermined"; Foundation Foods splits it into unit and modifier ("fruit", "medium (2-2/3\" dia)").
    static ImportedPortion? Portion(JsonElement portion)
    {
        var unit = portion.TryGetProperty("measureUnit", out var measure) ? measure.GetProperty("name").GetString() ?? "" : "";
        var modifier = portion.TryGetProperty("modifier", out var m) ? m.GetString()?.Trim() ?? "" : "";
        var name = unit is "" or "undetermined" ? modifier : $"{unit} {modifier}".Trim();
        var amount = portion.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : 1;
        var grams = portion.GetProperty("gramWeight").GetDouble();

        if (name.Length == 0 || amount <= 0 || grams <= 0 || Measures.Contains(FirstWord(name)))
            return null;
        if (name.Length > AppDbContext.MaxPortionNameLength)
            name = name[..AppDbContext.MaxPortionNameLength];
        return new ImportedPortion(name, grams / amount);
    }

    static string FirstWord(string name) => name.Split([' ', ',', '('], 2)[0];
}
