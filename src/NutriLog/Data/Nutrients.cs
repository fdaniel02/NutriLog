namespace NutriLog.Data;

// The fixed list, seeded by the migrations. Imports and the code refer to the ids, so they never change;
// adding a nutrient takes a migration.
public static class Nutrients
{
    public const string Energy = "energy";
    public const string Protein = "protein";
    public const string Fat = "fat";
    public const string Carbohydrate = "carbohydrate";
    public const string Fibre = "fibre";
    public const string Sugars = "sugars";
    public const string Alcohol = "alcohol";

    public static readonly IReadOnlyList<Nutrient> All = Numbered(
    [
        New(Energy, "Energy", "kcal", NutrientGroup.Energy),

        New(Protein, "Protein", "g", NutrientGroup.Macros),
        New(Fat, "Fat", "g", NutrientGroup.Macros),
        New(Carbohydrate, "Carbohydrate", "g", NutrientGroup.Macros),
        New(Fibre, "Fibre", "g", NutrientGroup.Macros),
        New(Sugars, "Sugars", "g", NutrientGroup.Macros),
        New(Alcohol, "Alcohol", "g", NutrientGroup.Macros),

        New("saturated_fat", "Saturated fat", "g", NutrientGroup.FattyAcids),
        New("monounsaturated_fat", "Monounsaturated fat", "g", NutrientGroup.FattyAcids),
        New("polyunsaturated_fat", "Polyunsaturated fat", "g", NutrientGroup.FattyAcids),
        New("trans_fat", "Trans fat", "g", NutrientGroup.FattyAcids),
        New("cholesterol", "Cholesterol", "mg", NutrientGroup.FattyAcids),

        New("vitamin_a", "Vitamin A", "µg", NutrientGroup.Vitamins),
        New("vitamin_d", "Vitamin D", "µg", NutrientGroup.Vitamins),
        New("vitamin_e", "Vitamin E", "mg", NutrientGroup.Vitamins),
        New("vitamin_k", "Vitamin K", "µg", NutrientGroup.Vitamins),
        New("vitamin_b1", "Vitamin B1 (thiamin)", "mg", NutrientGroup.Vitamins),
        New("vitamin_b2", "Vitamin B2 (riboflavin)", "mg", NutrientGroup.Vitamins),
        New("vitamin_b3", "Vitamin B3 (niacin)", "mg", NutrientGroup.Vitamins),
        New("vitamin_b6", "Vitamin B6", "mg", NutrientGroup.Vitamins),
        New("vitamin_b12", "Vitamin B12", "µg", NutrientGroup.Vitamins),
        New("folate", "Folate", "µg", NutrientGroup.Vitamins),
        New("vitamin_c", "Vitamin C", "mg", NutrientGroup.Vitamins),

        New("sodium", "Sodium", "mg", NutrientGroup.Minerals),
        New("potassium", "Potassium", "mg", NutrientGroup.Minerals),
        New("calcium", "Calcium", "mg", NutrientGroup.Minerals),
        New("magnesium", "Magnesium", "mg", NutrientGroup.Minerals),
        New("phosphorus", "Phosphorus", "mg", NutrientGroup.Minerals),
        New("iron", "Iron", "mg", NutrientGroup.Minerals),
        New("zinc", "Zinc", "mg", NutrientGroup.Minerals),
        New("copper", "Copper", "mg", NutrientGroup.Minerals),
        New("selenium", "Selenium", "µg", NutrientGroup.Minerals),
        New("iodine", "Iodine", "µg", NutrientGroup.Minerals),
    ]);

    static Nutrient New(string id, string name, string unit, NutrientGroup group) =>
        new() { Id = id, Name = name, Unit = unit, Group = group };

    static Nutrient[] Numbered(Nutrient[] nutrients)
    {
        for (var i = 0; i < nutrients.Length; i++)
            nutrients[i].SortOrder = i + 1;
        return nutrients;
    }
}
