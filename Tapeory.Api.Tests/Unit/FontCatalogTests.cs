using Tapeory.Api.Rendering;

namespace Tapeory.Api.Tests.Unit;

public sealed class FontCatalogTests
{
    [Fact]
    public void Families_SkipVerticalAliasesSymbolFontsAndUnknownNames()
    {
        var catalog = new FontCatalog(() => ["@Arial", ".SF NS", "Wingdings", "Definitely Not An Installed Font 7f3a", ""]);

        Assert.Empty(catalog.Families);
    }

    [Fact]
    public void Find_ReturnsNull_ForFamiliesOutsideTheCatalog()
    {
        var catalog = new FontCatalog(() => []);

        Assert.Null(catalog.Find("Arial"));
        Assert.Null(catalog.Find(null));
        Assert.Null(catalog.GetFontFile("Arial", bold: false));
    }

    [Fact]
    public void SystemCatalog_ServesARealFontFile_ForEveryListedFamilyItChecks()
    {
        var catalog = new FontCatalog();

        // Machines without any fonts (bare CI images) simply have nothing to serve.
        foreach (var family in catalog.Families.Take(3))
        {
            var file = catalog.GetFontFile(family, bold: false);
            Assert.NotNull(file);
            Assert.StartsWith("font/", file.ContentType);
            Assert.True(file.Data.Length > 100);
        }
    }

    [Theory]
    [InlineData(new byte[] { (byte)'O', (byte)'T', (byte)'T', (byte)'O' }, "font/otf")]
    [InlineData(new byte[] { 0, 1, 0, 0 }, "font/ttf")]
    [InlineData(new byte[] { (byte)'t', (byte)'t', (byte)'c', (byte)'f' }, "font/collection")]
    [InlineData(new byte[] { (byte)'w', (byte)'O', (byte)'F', (byte)'2' }, "font/woff2")]
    [InlineData(new byte[] { 1 }, "application/octet-stream")]
    public void DetectContentType_RecognizesFontFormats(byte[] header, string expected)
    {
        Assert.Equal(expected, FontCatalog.DetectContentType(header));
    }
}
