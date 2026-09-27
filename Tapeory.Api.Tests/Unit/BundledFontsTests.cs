using SkiaSharp;
using Tapeory.Api.Rendering;

namespace Tapeory.Api.Tests.Unit;

public sealed class BundledFontsTests
{
    [Theory]
    [InlineData("Roboto")]
    [InlineData("Open Sans")]
    [InlineData("Montserrat")]
    [InlineData("Bebas Neue")]
    public void ShipsTheFamily_WithSeparateRegularAndBoldFaces(string family)
    {
        Assert.Contains(family, BundledFonts.FamilyNames);

        using var regular = BundledFonts.Typeface(family, bold: false);
        using var bold = BundledFonts.Typeface(family, bold: true);

        Assert.Equal(family, regular!.FamilyName);
        Assert.Equal((int)SKFontStyleWeight.Normal, regular.FontWeight);
        Assert.Equal((int)SKFontStyleWeight.Bold, bold!.FontWeight);
    }

    [Fact]
    public void FontResolver_PrefersTheBundledFont_OverTheSystem()
    {
        using var typeface = FontResolver.Typeface("Lato", bold: false);

        Assert.Equal("Lato", typeface.FamilyName);
    }

    [Fact]
    public void FontCatalog_ListsAndServesTheBundledFonts()
    {
        var catalog = new FontCatalog();

        Assert.Equal("Inter", catalog.Find("inter"));
        var regular = catalog.GetFontFile("Inter", bold: false);
        var bold = catalog.GetFontFile("Inter", bold: true);
        Assert.Equal("font/otf", regular!.ContentType);
        Assert.NotEqual(regular.Data, bold!.Data);
    }
}
