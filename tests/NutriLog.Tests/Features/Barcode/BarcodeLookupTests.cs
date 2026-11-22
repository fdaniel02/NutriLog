using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NutriLog.Data;
using NutriLog.Features.Barcode;
using NutriLog.Tests.Data;

namespace NutriLog.Tests.Features.Barcode;

[Collection("Postgres")]
public class BarcodeLookupTests(PostgresFixture postgres)
{
    static CancellationToken Token => TestContext.Current.CancellationToken;

    sealed class FakeOpenFoodFacts(Func<string, OffProduct?> answer) : IOpenFoodFacts
    {
        public List<string> Asked { get; } = [];

        public Task<OffProduct?> GetProductAsync(string code, CancellationToken cancellationToken = default)
        {
            Asked.Add(code);
            return Task.FromResult(answer(code));
        }
    }

    static readonly OffProduct Crackers = new(OffSamples.Code, "Oat crackers", FoodBasis.Grams,
        new Dictionary<string, double> { [Nutrients.Energy] = 420, [Nutrients.Protein] = 10.5 }, new OffPortion("3 crackers", 25));

    static async Task<(BarcodeLookup Lookup, IDbContextFactory<AppDbContext> Factory)> CreateAsync(
        PostgresFixture postgres, IOpenFoodFacts off, FakeLogger<BarcodeLookup>? logger = null)
    {
        var factory = await MigratedDatabase.CreateAsync(postgres);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero));
        return (new BarcodeLookup(factory, off, time, logger is null ? NullLogger<BarcodeLookup>.Instance : logger), factory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("12345678901")]
    [InlineData("87123456789a6")]
    public async Task An_invalid_barcode_is_not_looked_up(string code)
    {
        var off = new FakeOpenFoodFacts(_ => Crackers);
        var (lookup, _) = await CreateAsync(postgres, off);

        Assert.Equal(new LookupResult(LookupStatus.Invalid), await lookup.LookupAsync(code, Token));
        Assert.Empty(off.Asked);
    }

    [Fact]
    public async Task Saves_a_new_product_as_an_open_food_facts_food_and_finds_it_there_next_time()
    {
        var off = new FakeOpenFoodFacts(_ => Crackers);
        var (lookup, factory) = await CreateAsync(postgres, off);

        var first = await lookup.LookupAsync($" {OffSamples.Code} ", Token);
        var second = await lookup.LookupAsync(OffSamples.Code, Token);

        Assert.Equal(LookupStatus.Found, first.Status);
        Assert.Equal(first, second);
        Assert.Single(off.Asked);
        await using var db = await factory.CreateDbContextAsync(Token);
        var food = await db.Foods.Include(f => f.Nutrients).Include(f => f.Portions).SingleAsync(Token);
        Assert.Equal((first.FoodId!.Value, "Oat crackers", FoodSource.Off, OffSamples.Code, OffSamples.Code),
            (food.Id, food.Name, food.Source, food.SourceId, food.Barcode));
        Assert.Equal([(Nutrients.Energy, 420.0), (Nutrients.Protein, 10.5)], food.Nutrients.OrderBy(n => n.NutrientId).Select(n => (n.NutrientId, n.Amount)));
        Assert.Equal(("3 crackers", 25.0, true), (food.Portions[0].Name, food.Portions[0].Grams, food.Portions[0].Imported));
    }

    [Fact]
    public async Task A_food_with_the_barcode_in_our_database_comes_first()
    {
        var off = new FakeOpenFoodFacts(_ => Crackers);
        var (lookup, factory) = await CreateAsync(postgres, off);
        int ownId;
        await using (var db = await factory.CreateDbContextAsync(Token))
        {
            var own = new Food { Name = "My crackers", Source = FoodSource.Own, Barcode = OffSamples.Code };
            db.Foods.Add(own);
            await db.SaveChangesAsync(Token);
            ownId = own.Id;
        }

        Assert.Equal(new LookupResult(LookupStatus.Found, ownId), await lookup.LookupAsync(OffSamples.Code, Token));
        Assert.Empty(off.Asked);
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var (lookup, factory) = await CreateAsync(postgres, new FakeOpenFoodFacts(_ => null));

        Assert.Equal(new LookupResult(LookupStatus.NotFound), await lookup.LookupAsync(OffSamples.Code, Token));
        await using var db = await factory.CreateDbContextAsync(Token);
        Assert.False(await db.Foods.AnyAsync(Token));
    }

    [Theory]
    [InlineData("refused")]
    [InlineData("bad json")]
    [InlineData("timeout")]
    public async Task Open_food_facts_failing_is_unavailable_and_logged(string kind)
    {
        Exception failure = kind switch
        {
            "refused" => new HttpRequestException(kind),
            "bad json" => new System.Text.Json.JsonException(kind),
            _ => new TaskCanceledException(kind)
        };
        var logger = new FakeLogger<BarcodeLookup>();
        var (lookup, _) = await CreateAsync(postgres, new FakeOpenFoodFacts(_ => throw failure), logger);

        Assert.Equal(new LookupResult(LookupStatus.Unavailable), await lookup.LookupAsync(OffSamples.Code, Token));
        Assert.Equal(30, logger.LatestRecord.Id.Id);
        Assert.Same(failure, logger.LatestRecord.Exception);
    }

    [Fact]
    public async Task A_cancelled_lookup_is_not_unavailable()
    {
        using var cancel = new CancellationTokenSource();
        var (lookup, _) = await CreateAsync(postgres, new FakeOpenFoodFacts(_ =>
        {
            cancel.Cancel();
            throw new TaskCanceledException();
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup.LookupAsync(OffSamples.Code, cancel.Token));
    }
}
