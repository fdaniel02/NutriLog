using NutriLog.Data;

namespace NutriLog.Features.Diary;

public sealed record DiaryDay(DateOnly Date, IReadOnlyList<DiaryItem> Entries, NutrientTotals Totals);

public sealed record DiaryItem(int Id, TimeOnly Time, string Name, string Amount, NutrientTotals Nutrients);

// Grams, or a number of portions when PortionId is set.
public readonly record struct EntryAmount(double Quantity, int? PortionId);

public sealed record PortionChoice(int Id, string Name, double Grams);

public sealed record FoodChoice(
    int Id,
    string Name,
    FoodBasis Basis,
    FoodSource Source,
    IReadOnlyList<PortionChoice> Portions,
    IReadOnlyDictionary<string, double> Per100,
    EntryAmount? LastAmount)
{
    public string Unit => Basis == FoodBasis.Millilitres ? "ml" : "g";

    public double Grams(EntryAmount amount) =>
        amount.PortionId is { } id ? amount.Quantity * Portion(id).Grams : amount.Quantity;

    public string Describe(EntryAmount amount) => amount.PortionId is { } id
        ? $"{NutrientFormat.Amount(amount.Quantity)} × {Portion(id).Name} · {NutrientFormat.Amount(Grams(amount))} {Unit}"
        : $"{NutrientFormat.Amount(amount.Quantity)} {Unit}";

    public NutrientTotals Nutrients(EntryAmount amount) => NutrientTotals.ForAmount(Per100, Grams(amount));

    PortionChoice Portion(int id) => Portions.First(p => p.Id == id);
}

public sealed record FoodMatch(int Id, string Name, string? NameNl, FoodSource Source, FoodBasis Basis, double? EnergyPer100, bool Recent);

public sealed record EntryDetails(int Id, DateOnly Date, TimeOnly Time, FoodChoice Food, EntryAmount Amount);
