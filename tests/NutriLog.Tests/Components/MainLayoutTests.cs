using Bunit;
using NutriLog.Components.Layout;
using NutriLog.Features.Sources;

namespace NutriLog.Tests.Components;

public class MainLayoutTests : BunitContext
{
    IRenderedComponent<MainLayout> Render()
    {
        ComponentFactories.AddStub<SourceFooter>("<footer class=\"source-footer\">Sources</footer>");
        return Render<MainLayout>(p => p.Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"body\">Page</p>")));
    }

    [Fact]
    public void Shows_the_page_inside_main()
    {
        var layout = Render();

        Assert.Equal("Page", layout.Find("main > #body").TextContent);
    }

    [Fact]
    public void The_source_reference_follows_the_page()
    {
        Assert.Equal("Sources", Render().Find("main + footer.source-footer").TextContent);
    }

    [Fact]
    public void The_header_links_home()
    {
        var brand = Render().Find("header .brand");

        Assert.Equal(("NutriLog", ""), (brand.TextContent, brand.GetAttribute("href")));
    }

    [Fact]
    public void The_header_links_the_diary_the_recipes_and_the_targets()
    {
        var links = Render().FindAll("header nav a");

        Assert.Equal([("Diary", ""), ("Recipes", "recipes"), ("Targets", "targets")], links.Select(a => (a.TextContent, a.GetAttribute("href"))));
    }
}
