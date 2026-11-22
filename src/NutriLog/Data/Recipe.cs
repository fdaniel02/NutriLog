namespace NutriLog.Data;

public class Recipe
{
    public int Id { get; init; }
    public required string Name { get; set; }
    public double Servings { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public List<RecipeIngredient> Ingredients { get; init; } = [];
}

public class RecipeIngredient
{
    public int RecipeId { get; init; }
    public int FoodId { get; init; }
    public double Grams { get; set; }
}
