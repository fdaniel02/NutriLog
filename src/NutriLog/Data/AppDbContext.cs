using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace NutriLog.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public const int MaxNameLength = 200;
    public const int MaxPortionNameLength = 100;

    public DbSet<Nutrient> Nutrients => Set<Nutrient>();
    public DbSet<Food> Foods => Set<Food>();
    public DbSet<FoodNutrient> FoodNutrients => Set<FoodNutrient>();
    public DbSet<Portion> Portions => Set<Portion>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<DiaryEntry> DiaryEntries => Set<DiaryEntry>();
    public DbSet<DiaryEntryNutrient> DiaryEntryNutrients => Set<DiaryEntryNutrient>();
    public DbSet<DataImport> DataImports => Set<DataImport>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Nutrient>(e =>
        {
            e.Property(x => x.Id).HasMaxLength(30).IsUnicode(false).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(50);
            e.Property(x => x.Unit).HasMaxLength(10);
            e.Property(x => x.Group).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.HasData(Data.Nutrients.All);
        });

        modelBuilder.Entity<Food>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(MaxNameLength);
            e.Property(x => x.NameNl).HasMaxLength(MaxNameLength);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.SourceId).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.Barcode).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Basis).HasConversion<string>().HasMaxLength(15).IsUnicode(false);
            // Own foods have no source id, and PostgreSQL lets NULLs repeat in a unique index.
            e.HasIndex(x => new { x.Source, x.SourceId }).IsUnique();
            e.HasIndex(x => x.Barcode);
            e.HasMany(x => x.Nutrients).WithOne().HasForeignKey(x => x.FoodId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Portions).WithOne().HasForeignKey(x => x.FoodId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FoodNutrient>(e =>
        {
            e.HasKey(x => new { x.FoodId, x.NutrientId });
            e.HasOne<Nutrient>().WithMany().HasForeignKey(x => x.NutrientId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_FoodNutrients_Amount", "\"Amount\" >= 0"));
        });

        modelBuilder.Entity<Portion>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(MaxPortionNameLength);
            // Lets a diary entry's portion refer to its food too, so it can't name another food's portion.
            e.HasAlternateKey(x => new { x.Id, x.FoodId });
            e.ToTable(t => t.HasCheckConstraint("CK_Portions_Grams", "\"Grams\" > 0"));
        });

        modelBuilder.Entity<Recipe>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(MaxNameLength);
            e.HasMany(x => x.Ingredients).WithOne().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_Recipes_Servings", "\"Servings\" > 0"));
        });

        modelBuilder.Entity<RecipeIngredient>(e =>
        {
            e.HasKey(x => new { x.RecipeId, x.FoodId });
            e.HasOne<Food>().WithMany().HasForeignKey(x => x.FoodId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_RecipeIngredients_Grams", "\"Grams\" > 0"));
        });

        modelBuilder.Entity<DiaryEntry>(e =>
        {
            e.Property(x => x.Meal).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.QuantityUnit).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
            e.HasIndex(x => x.Date);
            // Restrict: a food or recipe that's in the diary stays, so past days keep their numbers.
            e.HasOne<Food>().WithMany().HasForeignKey(x => x.FoodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Recipe>().WithMany().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Portion>().WithMany().HasForeignKey(x => new { x.PortionId, x.FoodId })
                .HasPrincipalKey(x => new { x.Id, x.FoodId }).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Nutrients).WithOne().HasForeignKey(x => x.DiaryEntryId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_DiaryEntries_FoodOrRecipe", "(\"FoodId\" IS NULL) <> (\"RecipeId\" IS NULL)");
                t.HasCheckConstraint("CK_DiaryEntries_Portion", "(\"QuantityUnit\" = 'Portion') = (\"PortionId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_DiaryEntries_Servings", "(\"QuantityUnit\" = 'Servings') = (\"RecipeId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_DiaryEntries_Quantity", "\"Quantity\" > 0");
            });
        });

        modelBuilder.Entity<DiaryEntryNutrient>(e =>
        {
            e.HasKey(x => new { x.DiaryEntryId, x.NutrientId });
            e.HasOne<Nutrient>().WithMany().HasForeignKey(x => x.NutrientId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_DiaryEntryNutrients_PerServing", "\"PerServing\" >= 0"));
        });

        modelBuilder.Entity<DataImport>(e =>
        {
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Version).HasMaxLength(50);
            e.HasIndex(x => new { x.Source, x.ImportedUtc });
        });

        // Npgsql only writes timestamptz from DateTime values with Kind = Utc, and a value can carry the wrong Kind.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(DateTime)))
            property.SetValueConverter(utc);
    }
}
