using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Unit;

public sealed class TemplateMapperTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseTags_ReturnsEmptyArray_ForBlankInput(string? input)
    {
        Assert.Empty(TemplateMapper.ParseTags(input));
    }

    [Fact]
    public void ParseTags_SplitsAndTrimsCommaSeparatedValues()
    {
        var tags = TemplateMapper.ParseTags("shipping, retail ,  food-safety");

        Assert.Equal(["shipping", "retail", "food-safety"], tags);
    }

    [Fact]
    public void SerializeTags_ReturnsNull_ForNullOrEmptyArray()
    {
        Assert.Null(TemplateMapper.SerializeTags(null));
        Assert.Null(TemplateMapper.SerializeTags([]));
    }

    [Fact]
    public void SerializeTags_JoinsAndTrimsEntries_DroppingBlanks()
    {
        var result = TemplateMapper.SerializeTags([" shipping ", "", "retail"]);

        Assert.Equal("shipping,retail", result);
    }

    [Fact]
    public void TagsRoundTrip_ThroughSerializeAndParse()
    {
        string[] original = ["shipping", "retail", "food-safety"];

        var roundTripped = TemplateMapper.ParseTags(TemplateMapper.SerializeTags(original));

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void PreviewImageUrl_ReturnsNull_WhenNoFileId()
    {
        Assert.Null(TemplateMapper.PreviewImageUrl(null));
    }

    [Fact]
    public void PreviewImageUrl_BuildsUploadsImagePath_WhenFileIdPresent()
    {
        Assert.Equal("/api/uploads/images/42", TemplateMapper.PreviewImageUrl(42));
    }
}
