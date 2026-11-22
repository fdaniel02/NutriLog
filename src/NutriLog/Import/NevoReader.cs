using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NutriLog.Data;

namespace NutriLog.Import;

// Reads NEVO-online's main file (NEVO<year>_v<version>.csv): one food per line, fields separated by '|', the header
// and text fields in double quotes, decimal commas, and an empty field where a value is unknown.
public static partial class NevoReader
{
    const string VersionColumn = "NEVO-versie/NEVO-version";
    const string CodeColumn = "NEVO-code";
    const string DutchNameColumn = "Voedingsmiddelnaam/Dutch food name";
    const string NameColumn = "Engelse naam/Food name";
    const string QuantityColumn = "Hoeveelheid/Quantity";

    // NEVO's codes for our nutrients. FOL is in dietary folate equivalents; NIA is niacin itself, not NIAEQ.
    public static readonly IReadOnlyDictionary<string, string> NutrientCodes = new Dictionary<string, string>
    {
        ["ENERCC"] = Nutrients.Energy,
        ["PROT"] = Nutrients.Protein,
        ["FAT"] = Nutrients.Fat,
        ["CHO"] = Nutrients.Carbohydrate,
        ["FIBT"] = Nutrients.Fibre,
        ["SUGAR"] = Nutrients.Sugars,
        ["ALC"] = Nutrients.Alcohol,
        ["FASAT"] = "saturated_fat",
        ["FAMSCIS"] = "monounsaturated_fat",
        ["FAPU"] = "polyunsaturated_fat",
        ["FATRS"] = "trans_fat",
        ["CHORL"] = "cholesterol",
        ["VITA_RAE"] = "vitamin_a",
        ["VITD"] = "vitamin_d",
        ["VITE"] = "vitamin_e",
        ["VITK"] = "vitamin_k",
        ["THIA"] = "vitamin_b1",
        ["RIBF"] = "vitamin_b2",
        ["NIA"] = "vitamin_b3",
        ["VITB6"] = "vitamin_b6",
        ["VITB12"] = "vitamin_b12",
        ["FOL"] = "folate",
        ["VITC"] = "vitamin_c",
        ["NA"] = "sodium",
        ["K"] = "potassium",
        ["CA"] = "calcium",
        ["MG"] = "magnesium",
        ["P"] = "phosphorus",
        ["FE"] = "iron",
        ["ZN"] = "zinc",
        ["CU"] = "copper",
        ["SE"] = "selenium",
        ["ID"] = "iodine",
    };

    // The header names a nutrient column with its unit, e.g. "PROT (g)", "SE (µg)".
    [GeneratedRegex(@"^(?<code>\S+) \((?<unit>[^)]+)\)$")]
    private static partial Regex NutrientHeader();

    // "NEVO-Online 2025 9.0" -> "2025/9.0", the form RIVM's required reference uses.
    [GeneratedRegex(@"(?<year>\d{4}) (?<number>\d+\.\d+)")]
    private static partial Regex VersionPattern();

    public static ImportedDataset Read(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var header = reader.ReadLine() ?? throw new ImportFormatException("The NEVO file is empty.");
        var columns = Split(header);
        int Column(string name) =>
            Array.IndexOf(columns, name) is var index and >= 0
                ? index
                : throw new ImportFormatException($"The NEVO file has no column \"{name}\". Is it NEVO-online's main file (NEVO<year>_v<version>.csv)?");

        var version = Column(VersionColumn);
        var code = Column(CodeColumn);
        var dutchName = Column(DutchNameColumn);
        var name = Column(NameColumn);
        var quantity = Column(QuantityColumn);
        var nutrients = NutrientColumns(columns);

        var foods = new List<ImportedFood>();
        string? versionText = null;
        var lineNumber = 1;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.Length == 0)
                continue;
            var fields = Split(line);
            if (fields.Length != columns.Length)
                throw new ImportFormatException($"Line {lineNumber} has {fields.Length} fields, the header {columns.Length}.");
            versionText ??= fields[version];
            foods.Add(new ImportedFood(
                fields[code],
                fields[name],
                fields[dutchName].Length > 0 ? fields[dutchName] : null,
                Basis(fields[quantity], lineNumber),
                nutrients
                    .Where(n => fields[n.Column].Length > 0)
                    .ToDictionary(n => n.NutrientId, n => Number(fields[n.Column], lineNumber)),
                []));
        }

        return new ImportedDataset(FoodSource.Nevo, Version(versionText), foods);
    }

    static (int Column, string NutrientId)[] NutrientColumns(string[] columns)
    {
        var found = columns
            .Select((header, index) => (Match: NutrientHeader().Match(header), Index: index))
            .Where(c => c.Match.Success && NutrientCodes.ContainsKey(c.Match.Groups["code"].Value))
            .Select(c => (c.Index, NutrientCodes[c.Match.Groups["code"].Value]))
            .ToArray();
        var missing = NutrientCodes.Values.Except(found.Select(f => f.Item2)).ToList();
        if (missing.Count > 0)
            throw new ImportFormatException($"The NEVO file has no column for: {string.Join(", ", missing)}.");
        return found;
    }

    static FoodBasis Basis(string quantity, int lineNumber) => quantity switch
    {
        "per 100g" => FoodBasis.Grams,
        "per 100ml" => FoodBasis.Millilitres,
        _ => throw new ImportFormatException($"Line {lineNumber}: unknown quantity \"{quantity}\"."),
    };

    static double Number(string text, int lineNumber) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ImportFormatException($"Line {lineNumber}: \"{text}\" isn't a number.");

    static string Version(string? text)
    {
        var match = VersionPattern().Match(text ?? "");
        return match.Success
            ? $"{match.Groups["year"].Value}/{match.Groups["number"].Value}"
            : throw new ImportFormatException($"Can't read the NEVO version from \"{text}\".");
    }

    // Text fields may be quoted; NEVO's names contain no '|' or quotes, so no escaping is needed.
    static string[] Split(string line) =>
        line.Split('|').Select(f => f.Length >= 2 && f[0] == '"' && f[^1] == '"' ? f[1..^1] : f).ToArray();
}
