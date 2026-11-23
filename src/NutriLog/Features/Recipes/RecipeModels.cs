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
    public IReadOnlyDictionary<string, (double PerServing, bool Complete)> PerServingNutrients =>
        RecipeNutrients.Snapshot(Ingredients.Select(i => (i.Per100, i.Grams)), Servings);

    // The raw ingredients' weight, counting a millilitre as a gram.
    public double ServingGrams => Ingredients.Sum(i => i.Grams) / Servings;

    public RecipeSnapshot Snapshot => new(PerServingNutrients, ServingGrams);

    public NutrientTotals PerServing => NutrientTotals.ForServings(PerServingNutrients, 1);
}

// What a diary entry keeps of a recipe as it was logged.
public sealed record RecipeSnapshot(IReadOnlyDictionary<string, (double PerServing, bool Complete)> PerServing, double ServingGrams)
{
    public NutrientTotals Nutrients(RecipeAmount amount) => NutrientTotals.ForServings(PerServing, amount.Servings(ServingGrams));

    public string Describe(RecipeAmount amount)
    {
        var servings = NutrientFormat.Servings(amount.Servings(ServingGrams));
        return amount.Unit == RecipeUnit.Grams
            ? $"{NutrientFormat.Amount(amount.Quantity)} g · {servings}"
            : $"{servings} · {NutrientFormat.Amount(amount.Quantity * ServingGrams)} g";
    }
}

public enum RecipeUnit
{
    Servings,
    Grams
}

public readonly record struct RecipeAmount(double Quantity, RecipeUnit Unit)
{
    public static RecipeAmount InServings(double servings) => new(servings, RecipeUnit.Servings);

    public static RecipeAmount InGrams(double grams) => new(grams, RecipeUnit.Grams);

    public double Servings(double servingGrams) => Unit == RecipeUnit.Grams ? Quantity / servingGrams : Quantity;

    // As the diary lists it: in the unit it was logged in.
    public string Logged => Unit == RecipeUnit.Grams ? $"{NutrientFormat.Amount(Quantity)} g" : NutrientFormat.Servings(Quantity);
}

public sealed record RecipeMatch(int Id, string Name, double Servings, NutrientTotal EnergyPerServing);

public sealed record RecipeEntryDetails(int Id, DateOnly Date, TimeOnly Time, int RecipeId, string RecipeName, RecipeAmount Amount,
    RecipeSnapshot Snapshot);

public enum RecipeDeleteResult
{
    Deleted,
    NotFound,
    // Logged recipes stay, so their entries keep pointing at them.
    InDiary
}

public sealed class InvalidRecipeException(string message) : Exception(message);
