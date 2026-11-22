namespace NutriLog.Data;

// At least one of Minimum and Maximum is set; check constraints in AppDbContext enforce it.
public class NutrientTarget
{
    public required string NutrientId { get; init; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    // Added to both ends for every km run that day, e.g. extra protein.
    public double ExtraPerRunKm { get; set; }
}
