using System.Text.Json;
using System.Xml.Linq;
using Tapeory.Api.Import;

namespace Tapeory.Api.Tests.Unit;

public sealed class LbxObjectConverterTests
{
    private static readonly XNamespace Pt = "http://schemas.brother.info/ptouch/2007/lbx/main";
    private static readonly XNamespace Style = "http://schemas.brother.info/ptouch/2007/lbx/style";
    private static readonly XNamespace TextNs = "http://schemas.brother.info/ptouch/2007/lbx/text";
    private static readonly XNamespace BarcodeNs = "http://schemas.brother.info/ptouch/2007/lbx/barcode";
    private static readonly XNamespace ImageNs = "http://schemas.brother.info/ptouch/2007/lbx/image";

    private static XElement TextObject(
        string x = "6.0pt",
        string y = "-48.2pt",
        string width = "310.0pt",
        string height = "172.0pt",
        string content = "Hello",
        string objectName = "Text2",
        string fontName = "Gill Sans",
        string weight = "700",
        string size = "48.0pt",
        string align = "LEFT",
        string mergeField = "",
        int stringItemCount = 1)
    {
        var objectStyle = new XElement(
            Pt + "objectStyle",
            new XAttribute("x", x),
            new XAttribute("y", y),
            new XAttribute("width", width),
            new XAttribute("height", height),
            new XAttribute("angle", "0"),
            new XElement(
                Pt + "expanded",
                new XAttribute("objectName", objectName),
                new XAttribute("dbMergeFieldStyleName", mergeField)));

        var stringItems = Enumerable.Range(0, stringItemCount).Select(_ => new XElement(TextNs + "stringItem"));

        return new XElement(
            TextNs + "text",
            objectStyle,
            new XElement(
                TextNs + "ptFontInfo",
                new XElement(TextNs + "logFont", new XAttribute("name", fontName), new XAttribute("weight", weight)),
                new XElement(TextNs + "fontExt", new XAttribute("size", size), new XAttribute("textColor", "#123456"))),
            new XElement(TextNs + "textAlign", new XAttribute("horizontalAlignment", align)),
            stringItems,
            new XElement(Pt + "data", content));
    }

    private static XElement BarcodeObject(string objectName = "Bar Code4") =>
        new(
            BarcodeNs + "barcode",
            new XElement(
                Pt + "objectStyle",
                new XAttribute("x", "0pt"),
                new XAttribute("y", "0pt"),
                new XElement(Pt + "expanded", new XAttribute("objectName", objectName))));

    private static XElement ImageObject(string objectName = "Image5") =>
        new(
            ImageNs + "image",
            new XElement(
                Pt + "objectStyle",
                new XAttribute("x", "0pt"),
                new XAttribute("y", "0pt"),
                new XElement(Pt + "expanded", new XAttribute("objectName", objectName))));

    private static XDocument BuildDocument(string paperWidth, string paperHeight, params XElement[] objects) =>
        new(
            new XElement(
                Pt + "document",
                new XElement(
                    Pt + "body",
                    new XElement(
                        Style + "sheet",
                        new XElement(Style + "paper", new XAttribute("width", paperWidth), new XAttribute("height", paperHeight)),
                        new XElement(Pt + "objects", objects)))));

    private static JsonElement ParseObjects(string editorJson) =>
        JsonDocument.Parse(editorJson).RootElement.Clone().GetProperty("objects");

    [Fact]
    public void Convert_ReadsThePaperSizeAndConvertsPointsToMillimeters()
    {
        var doc = BuildDocument("175.7pt", "319.8pt", TextObject());

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(LbxUnits.PointsToMm(175.7m), result.WidthMm);
        Assert.Equal(LbxUnits.PointsToMm(319.8m), result.HeightMm);
    }

    [Fact]
    public void Convert_DefaultsPaperSize_AndWarns_WhenPaperElementIsMissing()
    {
        var doc = new XDocument(
            new XElement(Pt + "document", new XElement(Pt + "body", new XElement(Pt + "objects"))));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(50m, result.WidthMm);
        Assert.Equal(25m, result.HeightMm);
        Assert.Contains(result.Warnings, w => w.Contains("size"));
    }

    [Fact]
    public void Convert_ConvertsAPlainTextObject_WithPositionFontAndAlignment()
    {
        var doc = BuildDocument(
            "175.7pt", "319.8pt",
            TextObject(x: "6.0pt", y: "-48.2pt", width: "310.0pt", height: "172.0pt",
                content: "ALECTO.CA", objectName: "Text2", fontName: "Gill Sans", weight: "700",
                size: "48.0pt", align: "CENTER"));

        var result = LbxObjectConverter.Convert(doc);

        var objects = ParseObjects(result.EditorJson);
        Assert.Equal(1, objects.GetArrayLength());

        var obj = objects[0];
        Assert.Equal("text", obj.GetProperty("type").GetString());
        Assert.Equal("ALECTO.CA", obj.GetProperty("text").GetString());
        Assert.Equal(LbxUnits.PointsToMm(6.0m), obj.GetProperty("x").GetDecimal());
        Assert.Equal(LbxUnits.PointsToMm(-48.2m), obj.GetProperty("y").GetDecimal());
        Assert.Equal(LbxUnits.PointsToMm(310.0m), obj.GetProperty("width").GetDecimal());
        Assert.Equal("Gill Sans", obj.GetProperty("fontFamily").GetString());
        Assert.Equal("bold", obj.GetProperty("fontWeight").GetString());
        Assert.Equal(48.0m, obj.GetProperty("fontSize").GetDecimal());
        Assert.Equal("center", obj.GetProperty("align").GetString());
        Assert.Equal("#123456", obj.GetProperty("fill").GetString());
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Convert_MapsAFontWeightBelow700ToNormal()
    {
        var doc = BuildDocument("50pt", "25pt", TextObject(weight: "400"));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal("normal", ParseObjects(result.EditorJson)[0].GetProperty("fontWeight").GetString());
    }

    [Theory]
    [InlineData("LEFT", "left")]
    [InlineData("CENTER", "center")]
    [InlineData("RIGHT", "right")]
    [InlineData("", "left")]
    [InlineData("SOMETHING_UNKNOWN", "left")]
    public void Convert_MapsHorizontalAlignment(string source, string expected)
    {
        var doc = BuildDocument("50pt", "25pt", TextObject(align: source));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(expected, ParseObjects(result.EditorJson)[0].GetProperty("align").GetString());
    }

    [Fact]
    public void Convert_TreatsADatabaseMergeBoundTextObject_AsADynamicField()
    {
        var doc = BuildDocument(
            "50pt", "25pt",
            TextObject(content: "ACME Corp", objectName: "CustomerNameBox", mergeField: "CustomerName"));

        var result = LbxObjectConverter.Convert(doc);

        var obj = ParseObjects(result.EditorJson)[0];
        Assert.Equal("dynamicField", obj.GetProperty("type").GetString());
        Assert.Equal("CustomerName", obj.GetProperty("fieldName").GetString());
        Assert.Equal("CustomerNameBox", obj.GetProperty("label").GetString());
        Assert.Equal("ACME Corp", obj.GetProperty("defaultValue").GetString());
        Assert.True(obj.GetProperty("required").GetBoolean());

        Assert.Single(result.Fields);
        Assert.Equal("CustomerName", result.Fields[0].Name);
        Assert.Equal("ACME Corp", result.Fields[0].DefaultValue);
    }

    [Fact]
    public void Convert_DeduplicatesRepeatedMergeFieldNames_ButStillEmitsBothObjects()
    {
        var doc = BuildDocument(
            "50pt", "25pt",
            TextObject(objectName: "Box1", mergeField: "Sku", content: "A"),
            TextObject(objectName: "Box2", mergeField: "Sku", content: "B"));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Single(result.Fields);
        Assert.Equal(2, ParseObjects(result.EditorJson).GetArrayLength());
    }

    [Fact]
    public void Convert_WarnsAndSkips_ForBarcodeObjects()
    {
        var doc = BuildDocument("50pt", "25pt", BarcodeObject("Bar Code4"));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(0, ParseObjects(result.EditorJson).GetArrayLength());
        Assert.Contains(result.Warnings, w => w.Contains("Bar Code4") && w.Contains("barcode"));
    }

    [Fact]
    public void Convert_WarnsAndSkips_ForImageObjects()
    {
        var doc = BuildDocument("50pt", "25pt", ImageObject("Image5"));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(0, ParseObjects(result.EditorJson).GetArrayLength());
        Assert.Contains(result.Warnings, w => w.Contains("Image5") && w.Contains("TIFF"));
    }

    [Fact]
    public void Convert_WarnsAboutMixedFormatting_ButStillConverts_WhenMultipleStringItemsArePresent()
    {
        var doc = BuildDocument("50pt", "25pt", TextObject(stringItemCount: 2));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(1, ParseObjects(result.EditorJson).GetArrayLength());
        Assert.Contains(result.Warnings, w => w.Contains("multiple text styles"));
    }

    [Fact]
    public void Convert_ProcessesObjectsIndependently_SoOneUnsupportedObjectDoesNotBlockOthers()
    {
        var doc = BuildDocument(
            "50pt", "25pt",
            TextObject(objectName: "Good1", content: "First"),
            BarcodeObject("Unsupported"),
            TextObject(objectName: "Good2", content: "Second"));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(2, ParseObjects(result.EditorJson).GetArrayLength());
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Convert_DoesNotWarn_ForAGenuinelyEmptyButPresentObjectsElement()
    {
        // A <pt:objects/> element with zero children is just a blank label — nothing went
        // wrong, so unlike a *missing* pt:objects element (see the test below), this shouldn't
        // produce a warning.
        var doc = BuildDocument("50pt", "25pt");

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(0, ParseObjects(result.EditorJson).GetArrayLength());
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Convert_Warns_WhenTheObjectsElementIsMissingEntirely()
    {
        var doc = new XDocument(
            new XElement(
                Pt + "document",
                new XElement(Pt + "body", new XElement(Style + "sheet", new XElement(Style + "paper",
                    new XAttribute("width", "50pt"), new XAttribute("height", "25pt"))))));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Contains(result.Warnings, w => w.Contains("No objects"));
    }

    [Fact]
    public void Convert_ReturnsAnEmptyDocument_WhenThereIsNoRootElement()
    {
        var doc = new XDocument();

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(0, ParseObjects(result.EditorJson).GetArrayLength());
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void EmptyResult_ProducesAValidDocumentCarryingJustTheGivenWarning()
    {
        var result = LbxObjectConverter.EmptyResult("The archive was corrupt.");

        Assert.Equal(0, ParseObjects(result.EditorJson).GetArrayLength());
        Assert.Equal(["The archive was corrupt."], result.Warnings);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Convert_ProducesEditorJson_InCamelCase_MatchingTheFrontendFormat()
    {
        var doc = BuildDocument("50pt", "25pt", TextObject());

        var result = LbxObjectConverter.Convert(doc);

        Assert.Contains("\"formatVersion\"", result.EditorJson);
        Assert.Contains("\"widthMm\"", result.EditorJson);
        Assert.DoesNotContain("\"FormatVersion\"", result.EditorJson);
    }
}
