using System.IO.Compression;
using NutriLog.Data;
using NutriLog.Import;
using static NutriLog.Tests.Import.SampleFiles;

namespace NutriLog.Tests.Import;

public class UsdaReaderTests
{
    const string FoundationFile = "FoodData_Central_foundation_food_json_2099-04-30.zip";
    const string SrLegacyFile = "FoodData_Central_sr_legacy_food_json_2018-04.zip";

    static ImportedFood ReadOne(string rootKey, UsdaFood food) =>
        Assert.Single(UsdaReader.Read(UsdaZip(rootKey, food), FoundationFile).Foods);

    [Theory]
    [InlineData("FoundationFoods", FoundationFile, FoodSource.UsdaFoundation, "2099-04-30")]
    [InlineData("SRLegacyFoods", SrLegacyFile, FoodSource.UsdaSrLegacy, "2018-04")]
    public void Tells_the_dataset_and_its_release_apart(string rootKey, string fileName, FoodSource source, string version)
    {
        var data = UsdaReader.Read(UsdaZip(rootKey, new UsdaFood(42, "Test fruit, raw", [])), fileName);

        Assert.Equal((source, version), (data.Source, data.Version));
        Assert.Equal(("42", "Test fruit, raw", null, FoodBasis.Grams), data.Foods.Select(f => (f.SourceId, f.Name, f.NameNl, f.Basis)).Single());
    }

    [Fact]
    public void Maps_nutrient_ids_to_ours_and_leaves_others_out()
    {
        var food = ReadOne("SRLegacyFoods", new UsdaFood(1, "Test", new() { [1008] = 89, [1003] = 1.1, [1100] = 3, [1120] = 26 }));

        Assert.Equal(new Dictionary<string, double> { [Nutrients.Energy] = 89, [Nutrients.Protein] = 1.1, ["iodine"] = 3 }, food.Nutrients);
    }

    [Fact]
    public void Every_one_of_our_nutrients_has_an_id()
    {
        Assert.Equal(Nutrients.All.Select(n => n.Id).Order(), UsdaReader.NutrientIds.Keys.Order());
    }

    [Theory]
    [InlineData(new[] { 1008, 2048, 2047 }, 100)]
    [InlineData(new[] { 2048, 2047 }, 101)]
    [InlineData(new[] { 2047 }, 102)]
    public void Energy_falls_back_to_the_atwater_factors(int[] present, double expected)
    {
        var values = new Dictionary<int, double> { [1008] = 100, [2048] = 101, [2047] = 102 };
        var food = ReadOne("FoundationFoods", new UsdaFood(1, "Test", present.ToDictionary(id => id, id => values[id])));

        Assert.Equal(expected, food.Nutrients[Nutrients.Energy]);
    }

    [Theory]
    [InlineData(new[] { 1190, 1177 }, 50)]
    [InlineData(new[] { 1177 }, 40)]
    public void Folate_prefers_dietary_folate_equivalents(int[] present, double expected)
    {
        var values = new Dictionary<int, double> { [1190] = 50, [1177] = 40 };
        var food = ReadOne("FoundationFoods", new UsdaFood(1, "Test", present.ToDictionary(id => id, id => values[id])));

        Assert.Equal(expected, food.Nutrients["folate"]);
    }

    [Fact]
    public void Negative_carbohydrate_by_difference_is_zero()
    {
        var food = ReadOne("FoundationFoods", new UsdaFood(1, "Chicken, raw", new() { [1005] = -0.475 }));

        Assert.Equal(0, food.Nutrients[Nutrients.Carbohydrate]);
    }

    [Fact]
    public void Keeps_portions_that_count_things_and_drops_measures()
    {
        var food = ReadOne("SRLegacyFoods", new UsdaFood(1, "Test fruit, raw", [],
            SrPortion("NLEA serving", 126), SrPortion("medium (7\" to 7-7/8\" long)", 118), SrPortion("cup, sliced", 150),
            SrPortion("oz", 28.35), SrPortion("fl oz", 30), SrPortion("tbsp", 15), SrPortion("slice", 32),
            SrPortion("large", 136), SrPortion("large", 999), SrPortion("", 10)));

        Assert.Equal([("medium (7\" to 7-7/8\" long)", 118), ("slice", 32), ("large", 136)], food.Portions.Select(p => (p.Name, p.Grams)));
    }

    [Fact]
    public void Foundation_portions_join_unit_and_modifier()
    {
        var food = ReadOne("FoundationFoods", new UsdaFood(1, "Test", [],
            FoundationPortion("fruit", "medium (2-2/3\" dia)", 170), FoundationPortion("egg", "whole without shell", 50.3),
            FoundationPortion("RACC", "", 50), FoundationPortion("cup", "sifted", 120), FoundationPortion("slice", "", 30)));

        Assert.Equal([("fruit medium (2-2/3\" dia)", 170), ("egg whole without shell", 50.3), ("slice", 30)], food.Portions.Select(p => (p.Name, p.Grams)));
    }

    [Fact]
    public void A_portion_of_several_is_divided_and_unusable_ones_are_dropped()
    {
        var food = ReadOne("SRLegacyFoods", new UsdaFood(1, "Test", [],
            SrPortion("pieces", 30, amount: 3), SrPortion("cookie", 10, amount: 0), SrPortion("bar", 0)));

        var portion = Assert.Single(food.Portions);
        Assert.Equal(("pieces", 10.0), (portion.Name, portion.Grams));
    }

    [Fact]
    public void Long_portion_names_are_cut_to_fit()
    {
        var food = ReadOne("SRLegacyFoods", new UsdaFood(1, "Test", [], SrPortion("piece " + new string('x', 200), 10)));

        Assert.Equal(AppDbContext.MaxPortionNameLength, Assert.Single(food.Portions).Name.Length);
    }

    [Fact]
    public void A_zip_without_json_is_refused()
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            archive.CreateEntry("readme.txt");
        zip.Position = 0;

        var error = Assert.Throws<ImportFormatException>(() => UsdaReader.Read(zip, FoundationFile));

        Assert.Contains("JSON", error.Message);
    }

    [Fact]
    public void Another_dataset_is_refused()
    {
        var error = Assert.Throws<ImportFormatException>(() => UsdaReader.Read(UsdaZip("BrandedFoods"), FoundationFile));

        Assert.Contains("Foundation Foods", error.Message);
    }

    [Fact]
    public void A_renamed_file_is_refused()
    {
        var error = Assert.Throws<ImportFormatException>(() => UsdaReader.Read(UsdaZip("FoundationFoods"), "usda.zip"));

        Assert.Contains("usda.zip", error.Message);
    }
}
