using Tapeory.Api.Import;

namespace Tapeory.Api.Tests.Unit;

public sealed class LbxUnitsTests
{
    [Theory]
    [InlineData("175.7pt", 175.7)]
    [InlineData("58.0pt", 58.0)]
    [InlineData("0.50pt", 0.50)]
    [InlineData("0pt", 0)]
    [InlineData("-48.2pt", -48.2)]
    public void ParsePoints_ParsesTheNumericPrefix(string input, double expected)
    {
        Assert.Equal((decimal)expected, LbxUnits.ParsePoints(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("pt")]
    public void ParsePoints_ReturnsNull_ForUnparseableInput(string? input)
    {
        Assert.Null(LbxUnits.ParsePoints(input));
    }

    [Fact]
    public void ParsePoints_IsCaseInsensitiveAboutTheUnitSuffix()
    {
        Assert.Equal(10m, LbxUnits.ParsePoints("10PT"));
    }

    [Fact]
    public void PointsToMm_ConvertsUsingTheStandardPointDefinition()
    {
        // 1pt = 1/72in = 0.352778mm (rounded to 2 decimal places).
        Assert.Equal(35.28m, LbxUnits.PointsToMm(100m));
    }

    [Fact]
    public void ParsePointsAsMm_CombinesParsingAndConversion()
    {
        Assert.Equal(35.28m, LbxUnits.ParsePointsAsMm("100pt"));
    }

    [Fact]
    public void ParsePointsAsMm_ReturnsNull_WhenInputIsUnparseable()
    {
        Assert.Null(LbxUnits.ParsePointsAsMm("garbage"));
    }
}
