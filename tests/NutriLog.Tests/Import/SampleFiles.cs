using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NutriLog.Import;

namespace NutriLog.Tests.Import;

// Files in the sources' formats with made-up foods and values: NEVO's conditions don't allow passing its data on.
public static class SampleFiles
{
    // Nutrient columns the import ignores, as in NEVO's file.
    static readonly string[] OtherNevoCodes = ["WATER", "NIAEQ"];

    static readonly string[] NevoTextColumns =
    [
        "NEVO-versie/NEVO-version", "Voedingsmiddelgroep", "Food group", "NEVO-code",
        "Voedingsmiddelnaam/Dutch food name", "Engelse naam/Food name", "Synoniem", "Hoeveelheid/Quantity",
    ];

    public sealed record NevoRow(string Code, string Name, string NameNl, string Quantity, Dictionary<string, string> Values);

    // A header like NEVO's: text columns, then "CODE (unit)" per nutrient, including ones the import ignores.
    public static MemoryStream Nevo(params NevoRow[] rows) => Nevo("NEVO-Online 2099 1.0", rows);

    public static MemoryStream Nevo(string version, params NevoRow[] rows)
    {
        var nutrientCodes = NevoReader.NutrientCodes.Keys.Concat(OtherNevoCodes).ToList();
        var header = NevoTextColumns.Concat(nutrientCodes.Select(c => $"{c} ({(c == "ENERCC" ? "kcal" : "g")})"));
        var lines = new List<string> { string.Join('|', header.Select(h => $"\"{h}\"")) };
        foreach (var row in rows)
        {
            var fields = new[] { version, "Groep", "Group", row.Code, row.NameNl, row.Name, "", row.Quantity }
                .Concat(nutrientCodes.Select(c => row.Values.TryGetValue(c, out var v) ? $"\"{v}\"" : ""));
            lines.Add(string.Join('|', fields));
        }
        // NEVO's file is UTF-8 with Windows line endings.
        return new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));
    }

    public sealed record UsdaFood(int FdcId, string Description, Dictionary<int, double> Nutrients, params object[] Portions);

    public static object SrPortion(string modifier, double grams, double amount = 1) =>
        new { amount, modifier, gramWeight = grams, measureUnit = new { id = 9999, name = "undetermined" } };

    public static object FoundationPortion(string unit, string modifier, double grams, double amount = 1) =>
        new { amount, modifier, gramWeight = grams, measureUnit = new { id = 1000, name = unit } };

    public static MemoryStream UsdaZip(string rootKey, params UsdaFood[] foods)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [rootKey] = foods.Select(f => new
            {
                fdcId = f.FdcId,
                description = f.Description,
                dataType = "Test",
                foodNutrients = f.Nutrients.Select(n => new
                {
                    type = "FoodNutrient",
                    nutrient = new { id = n.Key, number = "000", name = "Nutrient", unitName = "g" },
                    amount = n.Value,
                }),
                foodPortions = f.Portions,
            }),
        });
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("FoodData_Central_test.json").Open();
            entry.Write(Encoding.UTF8.GetBytes(json));
        }
        zip.Position = 0;
        return zip;
    }
}
