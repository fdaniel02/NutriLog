namespace NutriLog.Data;

// A recipe entry's nutrients per serving as they were when it was logged, so a later change to the recipe leaves
// past days as they were. A nutrient no ingredient has a value for has no row.
public class DiaryEntryNutrient
{
    public int DiaryEntryId { get; init; }
    public required string NutrientId { get; init; }
    public double PerServing { get; set; }
    // False when some ingredients have no value for the nutrient, so PerServing is a lower bound.
    public bool Complete { get; set; }
}
