using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NutriLog.Data;
using NutriLog.Features.Sources;

namespace NutriLog.Tests.Features;

public class SourceFooterTests : BunitContext
{
    sealed class FakeQueries(Dictionary<FoodSource, string> versions) : ISourceQueries
    {
        public Task<IReadOnlyDictionary<FoodSource, string>> ImportedVersionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<FoodSource, string>>(versions);
    }

    IRenderedComponent<SourceFooter> Render(Dictionary<FoodSource, string> versions)
    {
        Services.AddSingleton<ISourceQueries>(new FakeQueries(versions));
        return Render<SourceFooter>();
    }

    [Fact]
    public void Shows_a_line_per_reference()
    {
        var footer = Render(new() { [FoodSource.Nevo] = "2025/9.0", [FoodSource.UsdaSrLegacy] = "2018-04" });

        Assert.Equal(
            ["Based on data from NEVO online version 2025/9.0, RIVM, Bilthoven and other data sources.", SourceReference.FoodDataCentral],
            footer.FindAll("footer.source-footer p").Select(p => p.TextContent));
    }

    [Fact]
    public void Shows_nothing_before_the_first_import()
    {
        var footer = Render([]);

        Assert.Empty(footer.FindAll("footer"));
    }
}
