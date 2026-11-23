using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Features.Recipes;

public static class RecipeUrls
{
    public const string List = "recipes";
    public const string New = "recipes/new";

    public static string Edit(int recipeId) => $"recipes/{recipeId}";

    public static string AddToDay(DateOnly date, int recipeId) =>
        $"add?date={DiaryUrls.Format(date)}&recipe={recipeId}";
}
