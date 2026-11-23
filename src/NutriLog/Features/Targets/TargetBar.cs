using NutriLog.Features.Diary;

namespace NutriLog.Features.Targets;

// Fill and MinimumMark are fractions of the bar, 0 to 1.
public readonly record struct TargetBar(double Fill, double? MinimumMark, TargetStatus? Status)
{
    // A maximum is the end of the bar; a minimum alone only when there is no maximum.
    public static TargetBar For(TargetRange range, NutrientTotal total)
    {
        var status = range.Evaluate(total);
        var scale = range.Maximum ?? range.Minimum;
        if (scale is not > 0)
            return new TargetBar(status is null ? 0 : 1, null, status);

        var fill = status switch
        {
            null => 0,
            TargetStatus.Over => 1,
            _ => Math.Clamp(total.Amount / scale.Value, 0, 1)
        };
        var mark = range is { Minimum: { } min, Maximum: not null } ? min / scale.Value : (double?)null;
        return new TargetBar(fill, mark, status);
    }
}
