using NutriLog.Data;
using NutriLog.Features.Diary;

namespace NutriLog.Features.Targets;

// The runs of one day, as RunStats reports them.
public sealed record RunDay(int Runs, double Kilometres, double Calories, int RunsWithoutCalories)
{
    public static readonly RunDay None = new(0, 0, 0, 0);
}

// Runs is null when RunStats couldn't be reached; the targets are then the base ones.
public sealed record DayTargets(IReadOnlyDictionary<string, TargetRange> Ranges, RunDay? Runs)
{
    public static readonly DayTargets Empty = new(new Dictionary<string, TargetRange>(), RunDay.None);

    public bool HasTargets => Ranges.Count > 0;

    public TargetRange? this[string nutrientId] => Ranges.TryGetValue(nutrientId, out var range) ? range : null;

    public string RunNote => Runs switch
    {
        null => "RunStats couldn't be reached: base targets.",
        { Runs: 0 } => "No runs: base targets.",
        var r => $"{(r.Runs == 1 ? "Run" : $"{r.Runs} runs")} {NutrientFormat.Amount(r.Kilometres)} km: +{NutrientFormat.Amount(r.Calories)} kcal"
                 + (r.RunsWithoutCalories > 0 ? $" ({r.RunsWithoutCalories} without calories)" : "") + "."
    };

    // The run calories raise the energy target; carbohydrate grows in the same proportion, so the extra energy goes
    // to carbs in their share of the base energy. Other nutrients grow by their amount per km.
    public static DayTargets For(IReadOnlyCollection<NutrientTarget> targets, RunDay? runs)
    {
        var run = runs ?? RunDay.None;
        var baseEnergy = targets.FirstOrDefault(t => t.NutrientId == Nutrients.Energy) is { } energy
            ? new TargetRange(energy.Minimum, energy.Maximum).Reference
            : null;
        var energyFactor = baseEnergy is > 0 ? 1 + run.Calories / baseEnergy.Value : 1;

        var ranges = targets.ToDictionary(t => t.NutrientId, t =>
        {
            var range = new TargetRange(t.Minimum, t.Maximum);
            range = t.NutrientId switch
            {
                Nutrients.Energy => range.Plus(run.Calories),
                Nutrients.Carbohydrate => range.Times(energyFactor),
                _ => range
            };
            return t.ExtraPerRunKm > 0 ? range.Plus(t.ExtraPerRunKm * run.Kilometres) : range;
        });
        return new DayTargets(ranges, runs);
    }
}
