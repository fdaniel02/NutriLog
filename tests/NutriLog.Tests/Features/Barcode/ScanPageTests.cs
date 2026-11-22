using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NutriLog.Data;
using NutriLog.Features.Barcode;
using NutriLog.Features.Diary;
using NutriLog.Features.Foods;

namespace NutriLog.Tests.Features.Barcode;

public class ScanPageTests : BunitContext
{
    static readonly DateOnly Today = new(2026, 11, 22);

    sealed class FakeLookup(LookupResult result) : IBarcodeLookup
    {
        public List<string> Codes { get; } = [];

        public Task<LookupResult> LookupAsync(string code, CancellationToken cancellationToken = default)
        {
            Codes.Add(code);
            return Task.FromResult(result);
        }
    }

    BunitJSModuleInterop module = null!;

    FakeLookup SetUp(LookupResult result, string camera = "unsupported")
    {
        var lookup = new FakeLookup(result);
        Services.AddSingleton<IBarcodeLookup>(lookup);
        Services.AddSingleton(new Clock(TimeZoneInfo.Utc, new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero))));
        module = JSInterop.SetupModule(i => i.Arguments.Count == 1 && i.Arguments[0] is string path
            && path.EndsWith("Features/Barcode/ScanPage.razor.js", StringComparison.Ordinal));
        module.Setup<string>("start", _ => true).SetResult(camera);
        module.SetupVoid("stop").SetVoidResult();
        Navigation.NavigateTo(BarcodeUrls.Scan(Today, Meal.Lunch));
        return lookup;
    }

    NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void A_typed_barcode_that_is_found_adds_its_food()
    {
        var lookup = SetUp(new LookupResult(LookupStatus.Found, 42));
        var page = Render<ScanPage>();

        page.Find("input[aria-label=Barcode]").Change(" 8712345678906 ");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Add(Today, Meal.Lunch, 42), Navigation.Uri));
        Assert.Equal(["8712345678906"], lookup.Codes);
    }

    [Fact]
    public async Task A_scanned_barcode_is_looked_up()
    {
        var lookup = SetUp(new LookupResult(LookupStatus.Found, 7), camera: "started");
        var page = Render<ScanPage>();
        page.WaitForAssertion(() => Assert.Equal("Point the camera at the barcode.", page.Find(".scan-note").TextContent));

        await page.InvokeAsync(() => page.Instance.OnDetected("87123456"));

        page.WaitForAssertion(() => Assert.EndsWith(DiaryUrls.Add(Today, Meal.Lunch, 7), Navigation.Uri));
        Assert.Equal(["87123456"], lookup.Codes);
    }

    [Fact]
    public void An_unknown_product_offers_to_add_it_from_the_label()
    {
        SetUp(new LookupResult(LookupStatus.NotFound));
        var page = Render<ScanPage>();

        page.Find("input[aria-label=Barcode]").Change("87123456");
        page.Find("form").Submit();

        page.WaitForAssertion(() =>
            Assert.Equal(FoodUrls.NewWithBarcode(Today, Meal.Lunch, "87123456"), page.Find(".add-link").GetAttribute("href")));
        Assert.Contains("doesn't know 87123456", page.Find("p[role=alert]").TextContent);
    }

    [Theory]
    [InlineData(LookupStatus.Invalid, "A barcode has 8, 12, 13 or 14 digits.")]
    [InlineData(LookupStatus.Unavailable, "Open Food Facts can't be reached right now. Try again, or add the food from its label.")]
    public void Says_why_nothing_was_found(LookupStatus status, string message)
    {
        SetUp(new LookupResult(status));
        var page = Render<ScanPage>();

        page.Find("input[aria-label=Barcode]").Change("123");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal(message, page.Find(".form-error").TextContent));
    }

    [Theory]
    [InlineData("unsupported", "This browser can't scan barcodes. Type the barcode instead.")]
    [InlineData("denied", "No access to the camera. Type the barcode instead.")]
    public void Without_a_camera_it_asks_to_type_the_barcode(string camera, string note)
    {
        SetUp(new LookupResult(LookupStatus.NotFound), camera);

        var page = Render<ScanPage>();

        page.WaitForAssertion(() => Assert.Equal(note, page.Find(".scan-note").TextContent));
        Assert.Contains("hidden", page.Find("video").ClassList);
    }

    [Fact]
    public async Task Leaving_the_page_stops_the_camera()
    {
        SetUp(new LookupResult(LookupStatus.NotFound), "started");
        var page = Render<ScanPage>();
        page.WaitForAssertion(() => Assert.DoesNotContain("hidden", page.Find("video").ClassList));

        await page.Instance.DisposeAsync();

        Assert.Single(module.Invocations, i => i.Identifier == "stop");
    }
}
