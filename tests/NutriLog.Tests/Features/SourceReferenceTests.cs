using NutriLog.Data;
using NutriLog.Features.Sources;

namespace NutriLog.Tests.Features;

public class SourceReferenceTests
{
    const string NevoOnly = "Based on data from NEVO online version 2025/9.0, RIVM, Bilthoven.";
    const string NevoAndOthers = "Based on data from NEVO online version 2025/9.0, RIVM, Bilthoven and other data sources.";

    static IReadOnlyList<string> Lines(params (FoodSource Source, string Version)[] imports) =>
        SourceReference.Lines(imports.ToDictionary(i => i.Source, i => i.Version));

    [Fact]
    public void Nothing_imported_needs_no_reference() => Assert.Empty(Lines());

    [Fact]
    public void Nevo_alone_uses_nevos_first_sentence() =>
        Assert.Equal([NevoOnly], Lines((FoodSource.Nevo, "2025/9.0")));

    [Fact]
    public void Nevo_with_usda_uses_the_other_sentence_and_names_fooddata_central() =>
        Assert.Equal([NevoAndOthers, SourceReference.FoodDataCentral],
            Lines((FoodSource.Nevo, "2025/9.0"), (FoodSource.UsdaSrLegacy, "2018-04"), (FoodSource.UsdaFoundation, "2026-10-29")));

    [Fact]
    public void Usda_alone_names_fooddata_central_once() =>
        Assert.Equal([SourceReference.FoodDataCentral], Lines((FoodSource.UsdaFoundation, "2026-10-29")));

    [Fact]
    public void Open_food_facts_comes_last() =>
        Assert.Equal([SourceReference.FoodDataCentral, SourceReference.OpenFoodFacts],
            SourceReference.Lines(new Dictionary<FoodSource, string> { [FoodSource.UsdaFoundation] = "2026-10-29" }, hasOpenFoodFacts: true));
}
