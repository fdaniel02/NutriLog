using System.Text.Json;
using NutriLog.Data;
using NutriLog.Features.Barcode;

namespace NutriLog.Tests.Features.Barcode;

public class OffMappingTests
{
    static OffProduct? Map(string json) => OffMapping.Product(OffSamples.Code, JsonDocument.Parse(json).RootElement);

    static OffProduct MapProduct(string productJson) =>
        Map($$"""{"status":1,"product":{{productJson}}}""")!;

    [Fact]
    public void Maps_the_product_to_our_units()
    {
        var product = Map(OffSamples.Product)!;

        Assert.Equal(("Oat crackers (Brandname)", FoodBasis.Grams), (product.Name, product.Basis));
        Assert.Equal(new OffPortion("3 crackers (25 g)", 25), product.Serving);
        Assert.Equal(420, product.Per100[Nutrients.Energy]);
        Assert.Equal(12, product.Per100[Nutrients.Fat]);
        Assert.Equal(1.5, product.Per100["saturated_fat"]);
        Assert.Equal(480, product.Per100["sodium"], 6);
        Assert.Equal(50, product.Per100["calcium"], 6);
        Assert.Equal(1.5, product.Per100["vitamin_b12"], 6);
    }

    [Fact]
    public void Leaves_out_alcohol_negative_values_and_values_per_serving()
    {
        var product = Map(OffSamples.Product)!;

        Assert.False(product.Per100.ContainsKey(Nutrients.Alcohol));
        Assert.False(product.Per100.ContainsKey("iron"));
        Assert.Equal(10, product.Per100.Count);
    }

    [Fact]
    public void An_unknown_product_is_null()
    {
        Assert.Null(Map(OffSamples.NotFound));
        Assert.Null(Map("""{"status":0,"product":{}}"""));
    }

    [Fact]
    public void Energy_in_kilojoules_only_is_converted_to_kcal()
    {
        var product = MapProduct("""{"nutriments":{"energy_100g":418.4}}""");

        Assert.Equal(100, product.Per100[Nutrients.Energy], 6);
    }

    [Fact]
    public void Sodium_comes_from_salt_when_missing()
    {
        var product = MapProduct("""{"nutriments":{"salt_100g":1.5}}""");

        Assert.Equal(600, product.Per100["sodium"], 6);
    }

    [Fact]
    public void A_drink_in_ml_has_values_per_100_ml()
    {
        var product = MapProduct("""{"product_name":"Orange juice","product_quantity_unit":"ml","nutriments":{}}""");

        Assert.Equal(FoodBasis.Millilitres, product.Basis);
        Assert.Empty(product.Per100);
        Assert.Null(product.Serving);
    }

    [Theory]
    [InlineData("""{"brands":"Brandname"}""", "Brandname 8712345678906")]
    [InlineData("""{}""", "Product 8712345678906")]
    [InlineData("""{"product_name":" ","product_name_en":"Crackers"}""", "Crackers")]
    [InlineData("""{"product_name":"Brandname crackers","brands":"Brandname"}""", "Brandname crackers")]
    public void Names_the_product_from_what_it_has(string productJson, string name) =>
        Assert.Equal(name, MapProduct(productJson).Name);

    [Fact]
    public void A_long_name_is_cut_and_a_long_serving_name_replaced()
    {
        var product = MapProduct($$"""{"product_name":"{{new string('x', 300)}}","serving_size":"{{new string('y', 150)}}","serving_quantity":30}""");

        Assert.Equal(AppDbContext.MaxNameLength, product.Name.Length);
        Assert.Equal(new OffPortion("serving", 30), product.Serving);
    }
}
