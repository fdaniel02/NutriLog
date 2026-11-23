using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Features.Recipes;

public sealed record RecipeIngredientInput(int FoodId, double Grams);

public sealed record RecipeInput(string Name, double Servings, IReadOnlyList<RecipeIngredientInput> Ingredients);

public sealed record RecipeIngredientDetails(int FoodId, string Name, FoodBasis Basis, double Grams, IReadOnlyDictionary<string, double> Per100)
{
    public string Unit => Basis == FoodBasis.Millilitres ? "ml" : "g";
}

public sealed record RecipeDetails(int Id, string Name, double Servings, IReadOnlyList<RecipeIngredientDetails> Ingredients)
{
    public IReadOnlyDictionary<string, (double PerServing, bool Complete)> Snapshot =>
        RecipeNutrients.Snapshot(Ingredients.Select(i => (i.Per100, i.Grams)), Servings);

    public NutrientTotals PerServing => NutrientTotals.ForServings(Snapshot, 1);
}

public sealed record RecipeMatch(int Id, string Name, double Servings, NutrientTotal EnergyPerServing);

public sealed record RecipeEntryDetails(int Id, DateOnly Date, TimeOnly Time, int RecipeId, string RecipeName, double Servings,
    IReadOnlyDictionary<string, (double PerServing, bool Complete)> PerServing)
{
    public NutrientTotals Nutrients(double servings) => NutrientTotals.ForServings(PerServing, servings);
}

public enum RecipeDeleteResult
{
    Deleted,
    NotFound,
    // Logged recipes stay, so their entries keep pointing at them.
    InDiary
}

public sealed class InvalidRecipeException(string message) : Exception(message);
