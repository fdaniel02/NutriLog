namespace NutriLog.Data;

public class Food
{
    public int Id { get; init; }
    public required string Name { get; set; }
    public FoodSource Source { get; set; }
    // The food's id in its source (NEVO code, FoodData Central id, Open Food Facts barcode); none for own foods.
    public string? SourceId { get; set; }
    public string? Barcode { get; set; }
    // Nutrient amounts are per 100 g, or per 100 ml for liquids.
    public FoodBasis Basis { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    // A nutrient without a row here is unknown for this food, which is not the same as 0.
    public List<FoodNutrient> Nutrients { get; init; } = [];
    public List<Portion> Portions { get; init; } = [];
}

public enum FoodSource
{
    Nevo,
    Usda,
    Off,
    Own
}

public enum FoodBasis
{
    Grams,
    Millilitres
}

public class FoodNutrient
{
    public int FoodId { get; init; }
    public required string NutrientId { get; init; }
    public double Amount { get; set; }
}

// E.g. "1 slice" = 35 g. For a food measured in ml, Grams holds the millilitres.
public class Portion
{
    public int Id { get; init; }
    public int FoodId { get; init; }
    public required string Name { get; set; }
    public double Grams { get; set; }
}
