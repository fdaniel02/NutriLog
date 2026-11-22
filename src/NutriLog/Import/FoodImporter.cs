using Microsoft.EntityFrameworkCore;
using NutriLog.Data;

namespace NutriLog.Import;

public sealed record ImportResult(int Added, int Updated, int Unchanged, int Removed, int Kept);

// Brings a source's foods in line with a dataset, in one transaction. Foods are matched by source id, so a new
// release updates them in place and diary entries keep pointing at the same food. Foods gone from the release are
// removed, unless a recipe or the diary uses them. Only imported portions are touched; portions added by hand stay.
public sealed class FoodImporter(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time)
{
    public async Task<ImportResult> ImportAsync(ImportedDataset data, CancellationToken cancellationToken = default)
    {
        var duplicate = data.Foods.GroupBy(f => f.SourceId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ImportFormatException($"Source id {duplicate.Key} appears more than once.");

        await using var strategyContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        // The pages' DbContext retries on failure, which only works with the whole transaction inside the strategy.
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(
            ct => ImportOnceAsync(data, ct), cancellationToken);
    }

    async Task<ImportResult> ImportOnceAsync(ImportedDataset data, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;

        var existing = await db.Foods
            .Where(f => f.Source == data.Source)
            .Include(f => f.Nutrients)
            .Include(f => f.Portions)
            .AsSplitQuery()
            .ToDictionaryAsync(f => f.SourceId!, ct);
        var usedFoods = await db.DiaryEntries.Where(e => e.FoodId != null).Select(e => e.FoodId!.Value)
            .Union(db.RecipeIngredients.Select(i => i.FoodId))
            .ToHashSetAsync(ct);
        var usedPortions = await db.DiaryEntries.Where(e => e.PortionId != null).Select(e => e.PortionId!.Value).ToHashSetAsync(ct);

        int added = 0, updated = 0, unchanged = 0, removed = 0, kept = 0;
        foreach (var imported in data.Foods)
        {
            if (existing.Remove(imported.SourceId, out var food))
            {
                if (Update(food, imported, usedPortions, db))
                {
                    food.UpdatedUtc = now;
                    updated++;
                }
                else
                    unchanged++;
            }
            else
            {
                db.Foods.Add(New(imported, data.Source, now));
                added++;
            }
        }

        foreach (var gone in existing.Values)
        {
            if (usedFoods.Contains(gone.Id))
                kept++;
            else
            {
                db.Foods.Remove(gone);
                removed++;
            }
        }

        var result = new ImportResult(added, updated, unchanged, removed, kept);
        db.DataImports.Add(new DataImport
        {
            Source = data.Source,
            Version = data.Version,
            ImportedUtc = now,
            Added = added,
            Updated = updated,
            Unchanged = unchanged,
            Removed = removed,
            Kept = kept,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    static Food New(ImportedFood imported, FoodSource source, DateTime now) => new()
    {
        Name = imported.Name,
        NameNl = imported.NameNl,
        Source = source,
        SourceId = imported.SourceId,
        Basis = imported.Basis,
        CreatedUtc = now,
        UpdatedUtc = now,
        Nutrients = imported.Nutrients.Select(n => new FoodNutrient { NutrientId = n.Key, Amount = n.Value }).ToList(),
        Portions = imported.Portions.Select(p => new Portion { Name = p.Name, Grams = p.Grams, Imported = true }).ToList(),
    };

    static bool Update(Food food, ImportedFood imported, HashSet<int> usedPortions, AppDbContext db)
    {
        var changed = false;
        if (food.Name != imported.Name || food.NameNl != imported.NameNl || food.Basis != imported.Basis)
        {
            (food.Name, food.NameNl, food.Basis) = (imported.Name, imported.NameNl, imported.Basis);
            changed = true;
        }

        foreach (var nutrient in food.Nutrients.ToList())
        {
            if (!imported.Nutrients.TryGetValue(nutrient.NutrientId, out var amount))
            {
                db.FoodNutrients.Remove(nutrient);
                changed = true;
            }
            else if (Differs(nutrient.Amount, amount))
            {
                nutrient.Amount = amount;
                changed = true;
            }
        }
        foreach (var (nutrientId, amount) in imported.Nutrients)
        {
            if (food.Nutrients.All(n => n.NutrientId != nutrientId))
            {
                food.Nutrients.Add(new FoodNutrient { NutrientId = nutrientId, Amount = amount });
                changed = true;
            }
        }

        var importedPortions = imported.Portions.ToDictionary(p => p.Name);
        foreach (var portion in food.Portions.Where(p => p.Imported).ToList())
        {
            if (importedPortions.Remove(portion.Name, out var match))
            {
                if (Differs(portion.Grams, match.Grams))
                {
                    portion.Grams = match.Grams;
                    changed = true;
                }
            }
            // A portion the diary uses stays, so those entries keep their weight.
            else if (!usedPortions.Contains(portion.Id))
            {
                db.Portions.Remove(portion);
                changed = true;
            }
        }
        foreach (var portion in importedPortions.Values)
        {
            food.Portions.Add(new Portion { Name = portion.Name, Grams = portion.Grams, Imported = true });
            changed = true;
        }

        return changed;
    }

    // The values come from the same text each time, so any real change is far larger than this.
    static bool Differs(double stored, double imported) => Math.Abs(stored - imported) > 1e-9;
}
