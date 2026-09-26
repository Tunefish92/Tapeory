using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Unit;

public sealed class TemplateGroupsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Cables", "Cables")]
    [InlineData("  Jam jars ", "Jam jars")]
    public void Normalize_TrimsAndTreatsBlankAsNoGroup(string? input, string? expected)
    {
        Assert.Equal(expected, TemplateGroups.Normalize(input));
    }

    [Fact]
    public void Validate_AcceptsAGroupExactlyAtTheLimit_EvenWithSurroundingWhitespace()
    {
        Assert.Null(TemplateGroups.Validate("  " + new string('a', TemplateGroups.MaxLength) + "  "));
    }

    [Fact]
    public void Validate_RejectsAGroupOverTheLimit()
    {
        Assert.NotNull(TemplateGroups.Validate(new string('a', TemplateGroups.MaxLength + 1)));
    }

    [Fact]
    public void Validate_AcceptsNoGroup()
    {
        Assert.Null(TemplateGroups.Validate(null));
    }
}
