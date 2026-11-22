using System.Text;
using NutriLog.Data;
using NutriLog.Import;
using static NutriLog.Tests.Import.SampleFiles;

namespace NutriLog.Tests.Import;

public class NevoReaderTests
{
    static NevoRow Row(string code = "9001", string quantity = "per 100g", Dictionary<string, string>? values = null) =>
        new(code, "Test fruit raw", "Testvrucht rauw", quantity, values ?? new() { ["ENERCC"] = "88", ["PROT"] = "1,2" });

    [Fact]
    public void Reads_names_code_basis_and_version()
    {
        var data = NevoReader.Read(Nevo(Row(), Row(code: "9002", quantity: "per 100ml")));

        Assert.Equal((FoodSource.Nevo, "2099/1.0"), (data.Source, data.Version));
        Assert.Equal(
            [("9001", "Test fruit raw", "Testvrucht rauw", FoodBasis.Grams), ("9002", "Test fruit raw", "Testvrucht rauw", FoodBasis.Millilitres)],
            data.Foods.Select(f => (f.SourceId, f.Name, f.NameNl, f.Basis)));
        Assert.All(data.Foods, f => Assert.Empty(f.Portions));
    }

    [Fact]
    public void Reads_decimal_commas_and_leaves_empty_values_out()
    {
        var food = Assert.Single(NevoReader.Read(Nevo(Row(values: new() { ["ENERCC"] = "88", ["PROT"] = "1,25", ["FIBT"] = "0", ["VITK"] = "" }))).Foods);

        Assert.Equal(
            new Dictionary<string, double> { [Nutrients.Energy] = 88, [Nutrients.Protein] = 1.25, [Nutrients.Fibre] = 0 },
            food.Nutrients);
    }

    [Fact]
    public void Maps_every_nevo_code_to_one_of_our_nutrients()
    {
        var values = NevoReader.NutrientCodes.Keys.ToDictionary(c => c, _ => "1");
        values["WATER"] = "70";
        values["NIAEQ"] = "2";

        var food = Assert.Single(NevoReader.Read(Nevo(Row(values: values))).Foods);

        Assert.Equal(Nutrients.All.Select(n => n.Id).Order(), food.Nutrients.Keys.Order());
    }

    [Fact]
    public void An_empty_quoted_field_is_empty()
    {
        var csv = "\"NEVO-versie/NEVO-version\"|\"NEVO-code\"|\"Voedingsmiddelnaam/Dutch food name\"|\"Engelse naam/Food name\"|\"Hoeveelheid/Quantity\"|"
                  + string.Join('|', NevoReader.NutrientCodes.Keys.Select(c => $"\"{c} (g)\"")) + "\r\n"
                  + "NEVO-Online 2099 1.0|9001|Test|Test|per 100g|\"\"|\"5\"" + new string('|', NevoReader.NutrientCodes.Count - 2) + "\r\n";

        var food = Assert.Single(NevoReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(csv))).Foods);

        Assert.Equal(new Dictionary<string, double> { [Nutrients.Protein] = 5 }, food.Nutrients);
    }

    [Fact]
    public void A_missing_dutch_name_is_null()
    {
        var food = Assert.Single(NevoReader.Read(Nevo(Row() with { NameNl = "" })).Foods);

        Assert.Null(food.NameNl);
    }

    [Theory]
    [InlineData("empty file", "empty")]
    [InlineData("another file", "NEVO-versie/NEVO-version")]
    [InlineData("unknown quantity", "per stuk")]
    [InlineData("text for a number", "veel")]
    [InlineData("no version number", "version")]
    public void A_file_it_cannot_read_says_why(string problem, string mentioned)
    {
        var file = problem switch
        {
            "empty file" => new MemoryStream(),
            "another file" => new MemoryStream(Encoding.UTF8.GetBytes("\"NEVO-code\"|\"Something\"\r\n")),
            "unknown quantity" => Nevo(Row(quantity: "per stuk")),
            "text for a number" => Nevo(Row(values: new() { ["PROT"] = "veel" })),
            _ => Nevo("NEVO-Online", Row()),
        };

        var error = Assert.Throws<ImportFormatException>(() => NevoReader.Read(file));

        Assert.Contains(mentioned, error.Message);
    }

    [Fact]
    public void A_file_without_one_of_our_nutrients_is_refused()
    {
        var header = "\"NEVO-versie/NEVO-version\"|\"NEVO-code\"|\"Voedingsmiddelnaam/Dutch food name\"|\"Engelse naam/Food name\"|\"Hoeveelheid/Quantity\"|\"PROT (g)\"\r\n";

        var error = Assert.Throws<ImportFormatException>(() => NevoReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(header))));

        Assert.Contains(Nutrients.Energy, error.Message);
    }

    [Fact]
    public void A_line_with_the_wrong_number_of_fields_is_refused()
    {
        var file = Nevo(Row());
        var text = Encoding.UTF8.GetString(file.ToArray()) + "NEVO-Online 2099 1.0|9002\r\n";

        var error = Assert.Throws<ImportFormatException>(() => NevoReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text))));

        Assert.Contains("Line 3", error.Message);
    }
}
