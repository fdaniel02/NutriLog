namespace NutriLog.Data;

// Either a food, in grams or portions, or a recipe, in servings; check constraints in AppDbContext enforce it.
public class DiaryEntry
{
    public int Id { get; init; }
    public DateOnly Date { get; set; }
    public Meal Meal { get; set; }
    public int? FoodId { get; set; }
    public int? RecipeId { get; set; }
    public double Quantity { get; set; }
    public QuantityUnit QuantityUnit { get; set; }
    public int? PortionId { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public enum Meal
{
    Breakfast,
    Lunch,
    Dinner,
    Snack
}

public enum QuantityUnit
{
    // Millilitres for a food measured in ml.
    Grams,
    Portion,
    Servings
}
