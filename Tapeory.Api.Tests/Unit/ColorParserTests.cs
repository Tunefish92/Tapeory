using Tapeory.Api.Rendering;
using SkiaSharp;

namespace Tapeory.Api.Tests.Unit;

public sealed class ColorParserTests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#ffffff", 255, 255, 255)]
    [InlineData("#ff0000", 255, 0, 0)]
    [InlineData("#00FF00", 0, 255, 0)]
    public void Parse_ParsesSixDigitHexColors(string input, byte r, byte g, byte b)
    {
        var result = ColorParser.Parse(input, SKColors.Magenta);

        Assert.Equal(new SKColor(r, g, b), result);
    }

    [Theory]
    [InlineData("#f00", 255, 0, 0)]
    [InlineData("#0f0", 0, 255, 0)]
    [InlineData("#123", 0x11, 0x22, 0x33)]
    public void Parse_ExpandsThreeDigitHexColors(string input, byte r, byte g, byte b)
    {
        var result = ColorParser.Parse(input, SKColors.Magenta);

        Assert.Equal(new SKColor(r, g, b), result);
    }

    [Fact]
    public void Parse_ReturnsTransparent_ForTheLiteralTransparentKeyword()
    {
        Assert.Equal(SKColors.Transparent, ColorParser.Parse("transparent", SKColors.Black));
        Assert.Equal(SKColors.Transparent, ColorParser.Parse("TRANSPARENT", SKColors.Black));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-color")]
    [InlineData("#12")]
    [InlineData("#1234")]
    [InlineData("#gggggg")]
    public void Parse_ReturnsTheFallback_ForUnparseableInput(string? input)
    {
        var fallback = new SKColor(1, 2, 3);

        Assert.Equal(fallback, ColorParser.Parse(input, fallback));
    }
}
