using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NutriLog.Data;

namespace NutriLog.Features.Barcode;

public enum LookupStatus
{
    Found,
    NotFound,
    Invalid,
    // Open Food Facts couldn't be reached or answered with an error.
    Unavailable
}

public sealed record LookupResult(LookupStatus Status, int? FoodId = null);

public interface IBarcodeLookup
{
    Task<LookupResult> LookupAsync(string code, CancellationToken cancellationToken = default);
}

public sealed class BarcodeLookup(
    IDbContextFactory<AppDbContext> dbFactory, IOpenFoodFacts openFoodFacts, TimeProvider time, ILogger<BarcodeLookup> logger) : IBarcodeLookup
{
    // EAN-8, UPC-A, EAN-13 and GTIN-14.
    public static bool IsValid(string code) => code.Length is 8 or 12 or 13 or 14 && code.All(char.IsAsciiDigit);

    public async Task<LookupResult> LookupAsync(string code, CancellationToken cancellationToken = default)
    {
        code = code.Trim();
        if (!IsValid(code))
            return new LookupResult(LookupStatus.Invalid);

        // Our own database first, so a product scanned once (or corrected since) works without Open Food Facts.
        if (await FindAsync(code, cancellationToken) is { } known)
            return new LookupResult(LookupStatus.Found, known);

        OffProduct? product;
        try
        {
            product = await openFoodFacts.GetProductAsync(code, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.OpenFoodFactsUnavailable(ex, code);
            return new LookupResult(LookupStatus.Unavailable);
        }
        if (product is null)
            return new LookupResult(LookupStatus.NotFound);

        return new LookupResult(LookupStatus.Found, await SaveAsync(product, cancellationToken));
    }

    async Task<int?> FindAsync(string code, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Foods.AsNoTracking()
            .Where(f => f.Barcode == code)
            .OrderBy(f => f.Source == FoodSource.Own ? 0 : 1).ThenBy(f => f.Id)
            .Select(f => (int?)f.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    async Task<int> SaveAsync(OffProduct product, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var food = new Food
        {
            Name = product.Name,
            Source = FoodSource.Off,
            SourceId = product.Code,
            Barcode = product.Code,
            Basis = product.Basis,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        food.Nutrients.AddRange(product.Per100.Select(n => new FoodNutrient { NutrientId = n.Key, Amount = n.Value }));
        if (product.Serving is { } serving)
            food.Portions.Add(new Portion { Name = serving.Name, Grams = serving.Grams, Imported = true });
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Foods.Add(food);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return food.Id;
        }
        // The same barcode scanned twice at once: the other request saved it first.
        catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            return await FindAsync(product.Code, cancellationToken) ?? throw new InvalidOperationException(
                $"Saving barcode {product.Code} conflicted with a food that isn't there.", ex);
        }
    }
}
