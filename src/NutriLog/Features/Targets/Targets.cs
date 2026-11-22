using Microsoft.EntityFrameworkCore;
using NutriLog.Data;

namespace NutriLog.Features.Targets;

public interface ITargets
{
    Task<IReadOnlyList<NutrientTarget>> GetAsync(CancellationToken cancellationToken = default);
    // Replaces all targets with these.
    Task SaveAsync(IReadOnlyCollection<NutrientTarget> targets, CancellationToken cancellationToken = default);
    Task<DayTargets> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default);
}

public sealed class Targets(IDbContextFactory<AppDbContext> dbFactory, IRunStats runStats) : ITargets
{
    public async Task<IReadOnlyList<NutrientTarget>> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.NutrientTargets.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(IReadOnlyCollection<NutrientTarget> targets, CancellationToken cancellationToken = default)
    {
        foreach (var target in targets)
            Validate(target);
        if (targets.DistinctBy(t => t.NutrientId).Count() != targets.Count)
            throw new ArgumentException("A nutrient can only have one target.", nameof(targets));

        await using var strategyContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        // The pages' DbContext retries on failure, which only works with the whole transaction inside the strategy.
        await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.NutrientTargets.ExecuteDeleteAsync(ct);
            db.NutrientTargets.AddRange(targets.Select(t => new NutrientTarget
            {
                NutrientId = t.NutrientId,
                Minimum = t.Minimum,
                Maximum = t.Maximum,
                ExtraPerRunKm = t.ExtraPerRunKm
            }));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, cancellationToken);
    }

    public async Task<DayTargets> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var targets = await GetAsync(cancellationToken);
        if (targets.Count == 0)
            return DayTargets.Empty;
        return DayTargets.For(targets, await runStats.GetDayAsync(date, cancellationToken));
    }

    static void Validate(NutrientTarget target)
    {
        if (!Nutrients.All.Any(n => n.Id == target.NutrientId))
            throw new ArgumentException($"Nutrient '{target.NutrientId}' doesn't exist.", nameof(target));
        if (target.Minimum is null && target.Maximum is null)
            throw new ArgumentException("A target needs a minimum, a maximum or both.", nameof(target));
        if (new[] { target.Minimum, target.Maximum, target.ExtraPerRunKm }.Any(v => v is { } x && (!double.IsFinite(x) || x < 0)))
            throw new ArgumentException("Targets can't be negative.", nameof(target));
        if (target.Minimum > target.Maximum)
            throw new ArgumentException("The minimum can't be above the maximum.", nameof(target));
    }
}
