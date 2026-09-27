using Tapeory.Api.Rendering;

namespace Tapeory.Api.Tests.Unit;

public sealed class LabelDocumentParserTests
{
    [Fact]
    public void Parse_ReadsDocumentDimensions()
    {
        var json = """{"formatVersion":1,"widthMm":62,"heightMm":29,"objects":[]}""";

        var result = LabelDocumentParser.Parse(json);

        Assert.Equal(62m, result.WidthMm);
        Assert.Equal(29m, result.HeightMm);
        Assert.Empty(result.Objects);
    }

    [Theory]
    [InlineData("""{"widthMm":0,"heightMm":25,"objects":[]}""")]
    [InlineData("""{"widthMm":-10,"heightMm":25,"objects":[]}""")]
    [InlineData("""{"heightMm":25,"objects":[]}""")]
    public void Parse_DefaultsWidth_WhenMissingOrNotPositive(string json)
    {
        var result = LabelDocumentParser.Parse(json);

        Assert.Equal(50m, result.WidthMm);
    }

    [Fact]
    public void Parse_ParsesATextObject()
    {
        var json = """
            {"widthMm":50,"heightMm":25,"objects":[
              {"type":"text","id":"a","x":5,"y":6,"rotation":0,"locked":false,"hidden":false,
               "text":"Hello","width":30,"height":8,"fontSize":12,"fontFamily":"Arial",
               "fontWeight":"bold","align":"center","fill":"#ff0000"}
            ]}
            """;

        var result = LabelDocumentParser.Parse(json);

        var obj = Assert.Single(result.Objects);
        var text = Assert.IsType<RenderableText>(obj);
        Assert.Equal("Hello", text.Text);
        Assert.Equal(5m, text.X);
        Assert.Equal(6m, text.Y);
        Assert.Equal(12m, text.FontSize);
        Assert.Equal("Arial", text.FontFamily);
        Assert.Equal("bold", text.FontWeight);
        Assert.Equal("center", text.Align);
        Assert.Equal("#ff0000", text.Fill);
    }

    [Fact]
    public void Parse_ParsesADynamicFieldObject()
    {
        var json = """
            {"widthMm":50,"heightMm":25,"objects":[
              {"type":"dynamicField","id":"a","x":0,"y":0,"rotation":0,"locked":false,"hidden":false,
               "fieldName":"sku","width":20,"height":8,"fontSize":10,"fontFamily":"Arial",
               "fontWeight":"normal","align":"left","fill":"#000000"}
            ]}
            """;

        var result = LabelDocumentParser.Parse(json);

        var field = Assert.IsType<RenderableDynamicField>(Assert.Single(result.Objects));
        Assert.Equal("sku", field.FieldName);
    }

    [Fact]
    public void Parse_ParsesARectObject()
    {
        var json = """
            {"widthMm":50,"heightMm":25,"objects":[
              {"type":"rect","id":"a","x":1,"y":2,"rotation":45,"locked":true,"hidden":false,
               "width":20,"height":12,"fill":"#ffffff","stroke":"#000000","strokeWidth":0.5,
               "cornerRadius":2}
            ]}
            """;

        var result = LabelDocumentParser.Parse(json);

        var rect = Assert.IsType<RenderableRect>(Assert.Single(result.Objects));
        Assert.Equal(20m, rect.Width);
        Assert.Equal(12m, rect.Height);
        Assert.Equal(45m, rect.Rotation);
        Assert.True(rect.Locked);
        Assert.Equal(2m, rect.CornerRadius);
    }

    [Fact]
    public void Parse_ParsesALineObjectsPoints()
    {
        var json = """
            {"widthMm":50,"heightMm":25,"objects":[
              {"type":"line","id":"a","x":0,"y":0,"rotation":0,"locked":false,"hidden":false,
               "points":[0,0,20,10],"stroke":"#000000","strokeWidth":0.5}
            ]}
            """;

        var result = LabelDocumentParser.Parse(json);

        var line = Assert.IsType<RenderableLine>(Assert.Single(result.Objects));
        Assert.Equal([0m, 0m, 20m, 10m], line.Points);
    }

    [Fact]
    public void Parse_ParsesAnImageObject()
    {
        var json = """
            {"widthMm":50,"heightMm":25,"objects":[
              {"type":"image","id":"a","x":0,"y":0,"rotation":0,"locked":false,"hidden":false,
               "width":20,"height":15,"uploadedFileId":42,"url":"/api/uploads/images/42"}
            ]}
            """;

        var result = LabelDocumentParser.Parse(json);

        var image = Assert.IsType<RenderableImage>(Assert.Single(result.Objects));
        Assert.Equal(42, image.UploadedFileId);
        Assert.Equal("/api/uploads/images/42", image.Url);
    }

    [Fact]
    public void Parse_SkipsUnrecognizedObjectTypes_WithoutFailingTheWholeDocument()
    {
        var json = """
            {"widthMm":50,"heightMm":25,"objects":[
              {"type":"hologram","id":"a"},
              {"type":"text","id":"b","text":"Still here","width":20,"height":8,"fontSize":10,
               "fontFamily":"Arial","fontWeight":"normal","align":"left","fill":"#000000",
               "x":0,"y":0,"rotation":0,"locked":false,"hidden":false}
            ]}
            """;

        var result = LabelDocumentParser.Parse(json);

        var text = Assert.IsType<RenderableText>(Assert.Single(result.Objects));
        Assert.Equal("Still here", text.Text);
    }

    [Fact]
    public void Parse_TreatsAMissingObjectsArray_AsEmpty()
    {
        var result = LabelDocumentParser.Parse("""{"widthMm":50,"heightMm":25}""");

        Assert.Empty(result.Objects);
    }

    [Fact]
    public void Parse_FillsInDefaults_ForMissingOptionalFields()
    {
        var json = """{"widthMm":50,"heightMm":25,"objects":[{"type":"text","id":"a"}]}""";

        var result = LabelDocumentParser.Parse(json);

        var text = Assert.IsType<RenderableText>(Assert.Single(result.Objects));
        Assert.Equal(string.Empty, text.Text);
        Assert.Equal("Arial", text.FontFamily);
        Assert.Equal("normal", text.FontWeight);
        Assert.Equal("left", text.Align);
        Assert.Equal("#000000", text.Fill);
        Assert.False(text.Locked);
        Assert.False(text.Hidden);
    }

    [Theory]
    [InlineData("shrink", TextFitMode.Shrink)]
    [InlineData("wrap", TextFitMode.Wrap)]
    [InlineData("none", TextFitMode.None)]
    public void Parse_ReadsTheFitMode_OfTextAndDynamicFields(string fit, TextFitMode expected)
    {
        var json = $$"""{"widthMm":50,"heightMm":25,"objects":[{"type":"text","fit":"{{fit}}"},{"type":"dynamicField","fieldName":"n","fit":"{{fit}}"}]}""";

        var result = LabelDocumentParser.Parse(json);

        Assert.Equal(expected, Assert.IsType<RenderableText>(result.Objects[0]).Fit);
        Assert.Equal(expected, Assert.IsType<RenderableDynamicField>(result.Objects[1]).Fit);
    }

    [Fact]
    public void Parse_TreatsAMissingFitMode_AsNone_ForOlderDocuments()
    {
        var result = LabelDocumentParser.Parse("""{"widthMm":50,"heightMm":25,"objects":[{"type":"text","text":"x"}]}""");

        Assert.Equal(TextFitMode.None, Assert.IsType<RenderableText>(Assert.Single(result.Objects)).Fit);
    }
}
