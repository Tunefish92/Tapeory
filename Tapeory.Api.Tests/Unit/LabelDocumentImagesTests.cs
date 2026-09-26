using System.Text.Json;
using Tapeory.Api.Backups;

namespace Tapeory.Api.Tests.Unit;

public sealed class LabelDocumentImagesTests
{
    private const string Document = """
        {"formatVersion":1,"widthMm":62,"heightMm":29,"objects":[
          {"id":"a","type":"text","text":"Hi"},
          {"id":"b","type":"image","uploadedFileId":7,"url":"/api/uploads/images/7","width":10,"height":10},
          {"id":"c","type":"image","uploadedFileId":9,"url":"/api/uploads/images/9","width":10,"height":10},
          {"id":"d","type":"image","uploadedFileId":7,"url":"/api/uploads/images/7","width":5,"height":5}
        ]}
        """;

    [Fact]
    public void FindImageFileIds_ReturnsEachReferencedFileOnce()
    {
        Assert.Equal([7, 9], LabelDocumentImages.FindImageFileIds(Document).Order());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"objects":{}}""")]
    [InlineData("""{"objects":[{"type":"image","uploadedFileId":"x"}]}""")]
    public void FindImageFileIds_IgnoresDocumentsWithoutUsableImages(string editorJson)
    {
        Assert.Empty(LabelDocumentImages.FindImageFileIds(editorJson));
    }

    [Fact]
    public void RemapImageFileIds_PointsImagesAtTheirNewFiles_AndKeepsEverythingElse()
    {
        var remapped = LabelDocumentImages.RemapImageFileIds(Document, new Dictionary<int, int> { [7] = 70 });

        using var parsed = JsonDocument.Parse(remapped);
        var objects = parsed.RootElement.GetProperty("objects").EnumerateArray().ToList();

        Assert.Equal("Hi", objects[0].GetProperty("text").GetString());
        Assert.Equal(70, objects[1].GetProperty("uploadedFileId").GetInt32());
        Assert.Equal("/api/uploads/images/70", objects[1].GetProperty("url").GetString());
        Assert.Equal(10, objects[1].GetProperty("width").GetInt32());
        // Not in the map (its file wasn't in the backup): unchanged.
        Assert.Equal(9, objects[2].GetProperty("uploadedFileId").GetInt32());
        Assert.Equal(70, objects[3].GetProperty("uploadedFileId").GetInt32());
        Assert.Equal(62, parsed.RootElement.GetProperty("widthMm").GetInt32());
    }

    [Fact]
    public void RemapImageFileIds_ReturnsInputUnchanged_WhenNothingToRemap()
    {
        Assert.Same(Document, LabelDocumentImages.RemapImageFileIds(Document, new Dictionary<int, int>()));
        Assert.Equal("not json", LabelDocumentImages.RemapImageFileIds("not json", new Dictionary<int, int> { [1] = 2 }));
    }
}
