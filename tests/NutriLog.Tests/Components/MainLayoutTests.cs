using Bunit;
using NutriLog.Components.Layout;

namespace NutriLog.Tests.Components;

public class MainLayoutTests : BunitContext
{
    IRenderedComponent<MainLayout> Render() =>
        Render<MainLayout>(p => p.Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"body\">Page</p>")));

    [Fact]
    public void Shows_the_page_inside_main()
    {
        var layout = Render();

        Assert.Equal("Page", layout.Find("main > #body").TextContent);
    }

    [Fact]
    public void The_header_links_home()
    {
        var brand = Render().Find("header .brand");

        Assert.Equal(("NutriLog", ""), (brand.TextContent, brand.GetAttribute("href")));
    }
}
