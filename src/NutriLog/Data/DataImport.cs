namespace NutriLog.Data;

// One row per import run. The latest per source names the version the app's source reference cites.
public class DataImport
{
    public int Id { get; init; }
    public FoodSource Source { get; init; }
    public required string Version { get; init; }
    public DateTime ImportedUtc { get; init; }
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Unchanged { get; init; }
    public int Removed { get; init; }
    // Foods gone from the new release that a recipe or the diary still uses.
    public int Kept { get; init; }
}
