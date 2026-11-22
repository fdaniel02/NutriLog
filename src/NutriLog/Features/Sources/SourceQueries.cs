using Microsoft.EntityFrameworkCore;
using NutriLog.Data;

namespace NutriLog.Features.Sources;

public interface ISourceQueries
{
    // The version of each source's latest import.
    Task<IReadOnlyDictionary<FoodSource, string>> ImportedVersionsAsync(CancellationToken cancellationToken = default);
    // Products from Open Food Facts aren't imported; they come one by one, from barcode scans.
    Task<bool> HasOpenFoodFactsAsync(CancellationToken cancellationToken = default);
}

public sealed class SourceQueries(IDbContextFactory<AppDbContext> dbFactory) : ISourceQueries
{
    public async Task<IReadOnlyDictionary<FoodSource, string>> ImportedVersionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var latest = await db.DataImports
            .GroupBy(i => i.Source)
            .Select(g => g.OrderByDescending(i => i.ImportedUtc).ThenByDescending(i => i.Id).First())
            .ToListAsync(cancellationToken);
        return latest.ToDictionary(i => i.Source, i => i.Version);
    }

    public async Task<bool> HasOpenFoodFactsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Foods.AnyAsync(f => f.Source == FoodSource.Off, cancellationToken);
    }
}
