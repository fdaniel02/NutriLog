using NutriLog.Data;

namespace NutriLog.Features.Sources;

// The references the data sources ask for. NEVO's conditions of use require one of their two sentences on any output
// based on NEVO; FoodData Central asks to be named as the source.
public static class SourceReference
{
    public const string FoodDataCentral =
        "U.S. Department of Agriculture, Agricultural Research Service. FoodData Central, 2019. fdc.nal.usda.gov.";

    public static IReadOnlyList<string> Lines(IReadOnlyDictionary<FoodSource, string> importedVersions)
    {
        var lines = new List<string>();
        if (importedVersions.TryGetValue(FoodSource.Nevo, out var nevo))
        {
            var others = importedVersions.Keys.Any(s => s != FoodSource.Nevo);
            lines.Add($"Based on data from NEVO online version {nevo}, RIVM, Bilthoven{(others ? " and other data sources" : "")}.");
        }
        if (importedVersions.ContainsKey(FoodSource.UsdaFoundation) || importedVersions.ContainsKey(FoodSource.UsdaSrLegacy))
            lines.Add(FoodDataCentral);
        return lines;
    }
}
