using NutriLog.Features.Diary;

namespace NutriLog.Features.Targets;

public enum TargetStatus
{
    Under,
    Within,
    Over,
    // Some foods have no value for the nutrient, and the known part alone doesn't settle it.
    Unsure
}

public readonly record struct TargetRange(double? Minimum, double? Maximum)
{
    // What a run day's extra energy is measured against: the middle of a range, or its only end.
    public double? Reference => (Minimum, Maximum) switch
    {
        ({ } min, { } max) => (min + max) / 2,
        _ => Minimum ?? Maximum
    };

    public TargetRange Plus(double amount) => new(Minimum + amount, Maximum + amount);

    public TargetRange Times(double factor) => new(Minimum * factor, Maximum * factor);

    // A total with gaps is a lower bound: it can show "over", but not "under" or that it stays below the maximum.
    public TargetStatus? Evaluate(NutrientTotal total)
    {
        if (total.Unknown)
            return null;
        if (Maximum is { } max && total.Amount > max)
            return TargetStatus.Over;
        if (Minimum is { } min && total.Amount < min)
            return total.Complete ? TargetStatus.Under : TargetStatus.Unsure;
        return total.Complete || Maximum is null ? TargetStatus.Within : TargetStatus.Unsure;
    }

    public string Describe() => (Minimum, Maximum) switch
    {
        ({ } min, { } max) => $"{NutrientFormat.Amount(min)}–{NutrientFormat.Amount(max)}",
        ({ } min, null) => $"≥ {NutrientFormat.Amount(min)}",
        (null, { } max) => $"≤ {NutrientFormat.Amount(max)}",
        _ => ""
    };
}
