using NutriLog.Data;

namespace NutriLog.Import;

// A source's foods as read from its file, before they meet the database. Nutrients are keyed by our nutrient ids.
public sealed record ImportedDataset(FoodSource Source, string Version, IReadOnlyList<ImportedFood> Foods);

public sealed record ImportedFood(
    string SourceId,
    string Name,
    string? NameNl,
    FoodBasis Basis,
    IReadOnlyDictionary<string, double> Nutrients,
    IReadOnlyList<ImportedPortion> Portions);

public sealed record ImportedPortion(string Name, double Grams);

public sealed class ImportFormatException(string message) : Exception(message);
