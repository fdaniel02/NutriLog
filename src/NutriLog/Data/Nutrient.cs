namespace NutriLog.Data;

public class Nutrient
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string Unit { get; set; }
    public NutrientGroup Group { get; set; }
    public int SortOrder { get; set; }
}

public enum NutrientGroup
{
    Energy,
    Macros,
    FattyAcids,
    Vitamins,
    Minerals
}
