namespace NutriLog.Data;

// Either a food, in grams or portions, or a recipe, in servings or grams; check constraints in AppDbContext enforce it.
public class DiaryEntry
{
    public int Id { get; init; }
    public DateOnly Date { get; set; }
    // Local time of day, so it doesn't shift with time zones or DST; CreatedUtc stays the moment it was logged.
    public TimeOnly TimeEaten { get; set; }
    public int? FoodId { get; set; }
    public int? RecipeId { get; set; }
    public double Quantity { get; set; }
    public QuantityUnit QuantityUnit { get; set; }
    public int? PortionId { get; set; }
    // A recipe's weight per serving when it was logged, like its nutrients, so a later change to the recipe
    // doesn't change what grams of it amount to.
    public double? ServingGrams { get; set; }
    public DateTime CreatedUtc { get; set; }

    public List<DiaryEntryNutrient> Nutrients { get; init; } = [];
}

public enum QuantityUnit
{
    // Millilitres for a food measured in ml; for a recipe, grams of the dish.
    Grams,
    Portion,
    Servings
}
