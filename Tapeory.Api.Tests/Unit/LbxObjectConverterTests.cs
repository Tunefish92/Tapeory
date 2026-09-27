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
    private static readonly XNamespace DrawNs = "http://schemas.brother.info/ptouch/2007/lbx/draw";

    private static XElement ShapeStyle(string objectName, string pen = "NULL", string brush = "NULL") =>
        new(Pt + "objectStyle",
            new XAttribute("x", "10pt"), new XAttribute("y", "5pt"),
            new XAttribute("width", "40pt"), new XAttribute("height", "20pt"), new XAttribute("angle", "0"),
            new XElement(Pt + "pen", new XAttribute("style", pen), new XAttribute("widthX", "1pt"), new XAttribute("color", "#FF0000")),
            new XElement(Pt + "brush", new XAttribute("style", brush), new XAttribute("color", "#00FF00")),
            new XElement(Pt + "expanded", new XAttribute("objectName", objectName)));

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

    private static XElement ImageObject(string objectName = "Image5", string? fileName = "Object0.bmp") =>
        new(
            ImageNs + "image",
            new XElement(
                Pt + "objectStyle",
                new XAttribute("x", "6.8pt"),
                new XAttribute("y", "25.3pt"),
                new XAttribute("width", "13.5pt"),
                new XAttribute("height", "17pt"),
                new XElement(Pt + "expanded", new XAttribute("objectName", objectName))),
            fileName is null
                ? null
                : new XElement(ImageNs + "imageStyle", new XAttribute("fileName", fileName)));

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
    public void Convert_WarnsAndSkips_BarcodesWithoutAKnownType()
    {
        var doc = BuildDocument("50pt", "25pt", BarcodeObject("Bar Code4"));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(0, ParseObjects(result.EditorJson).GetArrayLength());
        Assert.Contains(result.Warnings, w => w.Contains("Bar Code4") && w.Contains("barcode"));
    }

    [Theory]
    [InlineData("CODE128", "code128")]
    [InlineData("EAN13", "ean13")]
    [InlineData("QRCODE", "qr")]
    [InlineData("DATAMATRIX", "datamatrix")]
    public void Convert_TurnsBarcodesIntoBarcodes_WithTheirTypeValueAndText(string protocol, string symbology)
    {
        var barcode = new XElement(BarcodeNs + "barcode",
            ShapeStyle("Bar Code1"),
            new XElement(BarcodeNs + "barcodeStyle", new XAttribute("protocol", protocol), new XAttribute("humanReadable", "true")),
            new XElement(Pt + "data", "4006381333931"));

        var result = LbxObjectConverter.Convert(BuildDocument("200pt", "50pt", barcode));

        var json = ParseObjects(result.EditorJson)[0];
        Assert.Equal("barcode", json.GetProperty("type").GetString());
        Assert.Equal(symbology, json.GetProperty("symbology").GetString());
        Assert.Equal("4006381333931", json.GetProperty("data").GetString());
        Assert.True(json.GetProperty("showText").GetBoolean());
        Assert.Equal("", json.GetProperty("fieldName").GetString());
    }

    [Fact]
    public void Convert_BindsAMergedBarcodeToAField()
    {
        var style = ShapeStyle("Bar Code1");
        style.Element(Pt + "expanded")!.SetAttributeValue("dbMergeFieldStyleName", "Serial");
        var barcode = new XElement(BarcodeNs + "barcode",
            style,
            new XElement(BarcodeNs + "barcodeStyle", new XAttribute("protocol", "CODE128")),
            new XElement(Pt + "data", "SN-1"));

        var result = LbxObjectConverter.Convert(BuildDocument("200pt", "50pt", barcode));

        Assert.Equal("Serial", ParseObjects(result.EditorJson)[0].GetProperty("fieldName").GetString());
        var field = Assert.Single(result.Fields);
        Assert.Equal(("Serial", "SN-1"), (field.Name, field.DefaultValue));
    }

    [Fact]
    public void Convert_TurnsRectanglesEllipsesAndLinesIntoShapes()
    {
        var rect = new XElement(DrawNs + "rect", ShapeStyle("Rectangle1", pen: "INSIDEFRAME", brush: "SOLID"),
            new XElement(DrawNs + "rectStyle", new XAttribute("shape", "ROUNDRECTANGLE"), new XAttribute("roundnessX", "4pt")));
        var ellipse = new XElement(DrawNs + "ellipse", ShapeStyle("Ellipse1", pen: "INSIDEFRAME"));
        var line = new XElement(DrawNs + "line", ShapeStyle("Line1", pen: "INSIDEFRAME"),
            new XElement(DrawNs + "lineStyle",
                new XAttribute("x1", "10pt"), new XAttribute("y1", "5pt"), new XAttribute("x2", "50pt"), new XAttribute("y2", "5pt")));

        var result = LbxObjectConverter.Convert(BuildDocument("200pt", "50pt", rect, ellipse, line));
        var objects = ParseObjects(result.EditorJson);

        Assert.Equal("rect", objects[0].GetProperty("type").GetString());
        Assert.Equal("#00FF00", objects[0].GetProperty("fill").GetString());
        Assert.Equal("#FF0000", objects[0].GetProperty("stroke").GetString());
        Assert.Equal(LbxUnits.ParsePointsAsMm("4pt"), objects[0].GetProperty("cornerRadius").GetDecimal());

        Assert.Equal("ellipse", objects[1].GetProperty("type").GetString());
        Assert.Equal("transparent", objects[1].GetProperty("fill").GetString());

        Assert.Equal("line", objects[2].GetProperty("type").GetString());
        var points = objects[2].GetProperty("points").EnumerateArray().Select(p => p.GetDecimal()).ToArray();
        Assert.Equal([0m, 0m, LbxUnits.ParsePointsAsMm("40pt")!.Value, 0m], points);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Convert_TurnsPTouchPolyLinesIntoLines_FromTheirPoints()
    {
        // As P-touch Editor saves a line (taken from a real .lbx): draw:poly with shape="LINE" and
        // its end points in page coordinates.
        var line = new XElement(DrawNs + "poly", ShapeStyle("Line3", pen: "INSIDEFRAME"),
            new XElement(DrawNs + "polyStyle", new XAttribute("shape", "LINE"),
                new XElement(DrawNs + "polyOrgPos", new XAttribute("x", "199.7pt"), new XAttribute("y", "32pt")),
                new XElement(DrawNs + "polyLinePoints", new XAttribute("points", "200.2pt,32.5pt 300.5pt,32.5pt"))));
        var polyline = new XElement(DrawNs + "poly", ShapeStyle("Poly2", pen: "INSIDEFRAME"),
            new XElement(DrawNs + "polyStyle", new XAttribute("shape", "POLYLINE"),
                new XElement(DrawNs + "polyLinePoints", new XAttribute("points", "10pt,10pt 20pt,10pt 20pt,20pt"))));

        var result = LbxObjectConverter.Convert(BuildDocument("400pt", "150pt", line, polyline));
        var objects = ParseObjects(result.EditorJson);

        Assert.Equal(3, objects.GetArrayLength()); // one line, plus a polyline's two segments
        Assert.All(objects.EnumerateArray(), o => Assert.Equal("line", o.GetProperty("type").GetString()));
        Assert.Equal(LbxUnits.ParsePointsAsMm("200.2pt"), objects[0].GetProperty("x").GetDecimal());
        var points = objects[0].GetProperty("points").EnumerateArray().Select(p => p.GetDecimal()).ToArray();
        Assert.Equal([0m, 0m, LbxUnits.ParsePointsAsMm("300.5pt")!.Value - LbxUnits.ParsePointsAsMm("200.2pt")!.Value, 0m], points);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Convert_ImportsFramesAsBorders_AndWarnsAboutFreeFormShapes()
    {
        var frame = new XElement(DrawNs + "frame", ShapeStyle("Frame1"));
        var poly = new XElement(DrawNs + "poly", ShapeStyle("Poly1"));

        var result = LbxObjectConverter.Convert(BuildDocument("200pt", "50pt", frame, poly));

        var border = Assert.Single(ParseObjects(result.EditorJson).EnumerateArray());
        Assert.Equal("rect", border.GetProperty("type").GetString());
        Assert.True(border.GetProperty("strokeWidth").GetDecimal() > 0);
        Assert.Contains(result.Warnings, w => w.Contains("Frame1") && w.Contains("border"));
        Assert.Contains(result.Warnings, w => w.Contains("Poly1") && w.Contains("free-form"));
    }

    [Fact]
    public void Convert_TurnsImageObjectsIntoImages_PointingAtTheirArchiveFile()
    {
        var doc = BuildDocument("50pt", "25pt", ImageObject("Image5", "Object0.bmp"));

        var result = LbxObjectConverter.Convert(doc);

        var image = Assert.Single(result.Images);
        Assert.Equal("Object0.bmp", image.SourceFileName);
        Assert.Equal("Image5", image.Name);
        Assert.Equal(LbxUnits.ParsePointsAsMm("6.8pt"), image.X);
        Assert.Equal(LbxUnits.ParsePointsAsMm("17pt"), image.Height);

        var json = ParseObjects(result.EditorJson)[0];
        Assert.Equal("image", json.GetProperty("type").GetString());
        Assert.False(json.TryGetProperty("sourceFileName", out _));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Convert_UsesTheTapeWidthAsHeight_AndSizesAnAutoLengthLabelToItsContent()
    {
        // A 24 mm tape label as P-touch Editor saves it: landscape paper 68pt (24 mm) wide with a
        // 1000 mm maximum length; the image spans x 6.8pt..20.3pt (2.4..7.2 mm).
        var doc = BuildDocument("68pt", "2834.4pt", ImageObject("Image5"));
        var paper = doc.Descendants(Style + "paper").Single();
        paper.SetAttributeValue("orientation", "landscape");
        paper.SetAttributeValue("autoLength", "true");

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(LbxUnits.ParsePointsAsMm("68pt"), result.HeightMm);
        Assert.Equal(Math.Round(LbxUnits.ParsePointsAsMm("20.3pt")!.Value + LbxUnits.ParsePointsAsMm("6.8pt")!.Value, 2), result.WidthMm);
    }

    [Fact]
    public void Convert_KeepsThePaperSize_ForAPortraitFixedLengthLabel()
    {
        var doc = BuildDocument("175.7pt", "319.8pt", ImageObject("Image5"));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Equal(LbxUnits.ParsePointsAsMm("175.7pt"), result.WidthMm);
        Assert.Equal(LbxUnits.ParsePointsAsMm("319.8pt"), result.HeightMm);
    }

    [Fact]
    public void Convert_WarnsAndSkips_ImagesThatDoNotNameTheirFile()
    {
        var doc = BuildDocument("50pt", "25pt", ImageObject("Image5", fileName: null));

        var result = LbxObjectConverter.Convert(doc);

        Assert.Empty(result.Images);
        Assert.Contains(result.Warnings, w => w.Contains("Image5"));
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
