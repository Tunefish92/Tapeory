using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Import;

public sealed record LbxConversionResult(
    decimal WidthMm,
    decimal HeightMm,
    ImportedLabelDocument Document,
    List<TemplateFieldDto> Fields,
    List<string> Warnings)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Images still waiting for LbxImportService to store their file.</summary>
    public IEnumerable<ImportedImageObject> Images => Document.Objects.OfType<ImportedImageObject>();

    public string EditorJson => JsonSerializer.Serialize(Document, Document.GetType(), JsonOptions);
}

/// <summary>
/// Converts a parsed label.xml into Tapeory's native editor document format.
///
/// Schema knowledge here comes from a real P-touch Editor export (Brother's own namespaces:
/// pt/style/text/draw/image/barcode/database/table under
/// http://schemas.brother.info/ptouch/2007/lbx/*), not an official spec — .lbx is proprietary
/// and undocumented. Matching is done by local element name only (ignoring namespace URIs) to
/// be more tolerant of schema variations across P-touch Editor versions.
///
/// Text objects (text:text), images (image:image), barcodes (barcode:barcode) and shapes
/// (draw:rect/ellipse/line, and frames as simple borders) are converted. A text object bound to a
/// P-touch "database merge" field (pt:expanded/@dbMergeFieldStyleName is non-empty) becomes a
/// Tapeory dynamic field instead of fixed text — this is the "named object" -> dynamic field
/// mapping described in the project plan. An image keeps its position and size and points at
/// its file in the archive (imageStyle/@fileName, a BMP or TIFF); LbxImportService converts and
/// stores that file. Barcode types Tapeory can't print and free-form shapes are left
/// unconverted with an explicit warning rather than guessed at, per the plan's "partial
/// conversion with explicit warnings instead of silently discarding" approach.
/// </summary>
public static class LbxObjectConverter
{
    private const decimal DefaultWidthMm = 50m;
    private const decimal DefaultHeightMm = 25m;

    public static LbxConversionResult Convert(XDocument labelXml)
    {
        var warnings = new List<string>();
        var root = labelXml.Root;

        if (root is null)
        {
            warnings.Add("label.xml had no root element; created an empty template.");
            return EmptyResult(warnings);
        }

        var (widthMm, heightMm) = ReadPaperSize(root, warnings);
        var objectsElement = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "objects");

        var imported = new List<object>();
        var fields = new List<TemplateFieldDto>();
        var seenFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (objectsElement is null)
        {
            warnings.Add("No objects were found in this label.");
        }
        else
        {
            var index = 0;

            foreach (var element in objectsElement.Elements())
            {
                index++;

                try
                {
                    ConvertObject(element, index, imported, fields, seenFieldNames, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Object #{index} ({element.Name.LocalName}) could not be converted: {ex.Message}");
                }
            }
        }

        (widthMm, heightMm) = ApplyOrientationAndAutoLength(root, widthMm, heightMm, imported);
        var document = new ImportedLabelDocument(1, widthMm, heightMm, imported);

        return new LbxConversionResult(widthMm, heightMm, document, fields, warnings);
    }

    /// <summary>Builds a valid (if empty) conversion result carrying just the given warning —
    /// used both when label.xml has no root element and when the archive couldn't be parsed at
    /// all, so an import never fails outright even for a file this parser can't make sense of.</summary>
    public static LbxConversionResult EmptyResult(string warning) => EmptyResult([warning]);

    private static LbxConversionResult EmptyResult(List<string> warnings)
    {
        var document = new ImportedLabelDocument(1, DefaultWidthMm, DefaultHeightMm, []);
        return new LbxConversionResult(DefaultWidthMm, DefaultHeightMm, document, [], warnings);
    }

    private static (decimal WidthMm, decimal HeightMm) ReadPaperSize(XElement root, List<string> warnings)
    {
        var paper = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "paper");

        var width = paper is null ? null : LbxUnits.ParsePointsAsMm((string?)paper.Attribute("width"));
        var height = paper is null ? null : LbxUnits.ParsePointsAsMm((string?)paper.Attribute("height"));

        if (width is null || height is null)
        {
            warnings.Add($"Could not determine the label size; defaulting to {DefaultWidthMm}×{DefaultHeightMm}mm.");
            return (DefaultWidthMm, DefaultHeightMm);
        }

        return (width.Value, height.Value);
    }

    /// <summary>
    /// Tape labels are usually stored as landscape paper: its width is the tape width and its
    /// height the length, while the objects are laid out along the tape. Swapping puts the
    /// length first, like the editor. With auto length the stored length is only a maximum (for
    /// example 1000 mm); the real label ends after its content, with as much space after the last
    /// object as before the first.
    /// </summary>
    private static (decimal WidthMm, decimal HeightMm) ApplyOrientationAndAutoLength(
        XElement root, decimal widthMm, decimal heightMm, List<object> imported)
    {
        var paper = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "paper");

        if (string.Equals((string?)paper?.Attribute("orientation"), "landscape", StringComparison.OrdinalIgnoreCase))
        {
            (widthMm, heightMm) = (heightMm, widthMm);
        }

        var autoLength = string.Equals((string?)paper?.Attribute("autoLength"), "true", StringComparison.OrdinalIgnoreCase);
        var spans = imported.Select(HorizontalSpan).OfType<(decimal Start, decimal End)>().ToList();

        if (autoLength && spans.Count > 0)
        {
            var start = Math.Max(spans.Min(span => span.Start), 0);
            var end = spans.Max(span => span.End);
            widthMm = Math.Min(widthMm, Math.Round(end + start, 2));
        }

        return (widthMm, heightMm);
    }

    private static (decimal Start, decimal End)? HorizontalSpan(object imported) => imported switch
    {
        ImportedTextObject text => (text.X, text.X + text.Width),
        ImportedDynamicFieldObject field => (field.X, field.X + field.Width),
        ImportedImageObject image => (image.X, image.X + image.Width),
        ImportedRectObject rect => (rect.X, rect.X + rect.Width),
        ImportedEllipseObject ellipse => (ellipse.X, ellipse.X + ellipse.Width),
        ImportedBarcodeObject barcode => (barcode.X, barcode.X + barcode.Width),
        ImportedLineObject line => (line.X + Math.Min(line.Points[0], line.Points[2]), line.X + Math.Max(line.Points[0], line.Points[2])),
        _ => null
    };

    private static void ConvertObject(
        XElement element,
        int index,
        List<object> imported,
        List<TemplateFieldDto> fields,
        HashSet<string> seenFieldNames,
        List<string> warnings)
    {
        var localName = element.Name.LocalName;
        var objectStyle = element.Elements().FirstOrDefault(e => e.Name.LocalName == "objectStyle");
        var objectName = ReadObjectName(objectStyle) ?? $"{localName} #{index}";

        if (localName is not ("text" or "image" or "barcode" or "rect" or "ellipse" or "line" or "frame" or "poly"))
        {
            warnings.Add($"'{objectName}' ({localName}) was not converted — {UnsupportedReason(localName)}");
            return;
        }

        if (objectStyle is null)
        {
            warnings.Add($"'{objectName}' had no position/size information and was skipped.");
            return;
        }

        var x = LbxUnits.ParsePointsAsMm((string?)objectStyle.Attribute("x")) ?? 0;
        var y = LbxUnits.ParsePointsAsMm((string?)objectStyle.Attribute("y")) ?? 0;
        var width = LbxUnits.ParsePointsAsMm((string?)objectStyle.Attribute("width")) ?? 20;
        var height = LbxUnits.ParsePointsAsMm((string?)objectStyle.Attribute("height")) ?? 8;
        var rotation = decimal.TryParse(
            (string?)objectStyle.Attribute("angle"), NumberStyles.Float, CultureInfo.InvariantCulture, out var angle)
            ? angle
            : 0;

        var id = $"lbx-{index}-{Guid.NewGuid():N}";

        switch (localName)
        {
            case "barcode":
                ConvertBarcode(element, objectStyle, id, objectName, x, y, width, height, rotation, imported, fields, seenFieldNames, warnings);
                return;
            case "rect" or "ellipse" or "frame":
                ConvertShape(element, objectStyle, id, objectName, localName, x, y, width, height, rotation, imported, warnings);
                return;
            case "line":
                ConvertLine(element, objectStyle, id, x, y, width, height, rotation, imported);
                return;
            case "poly":
                ConvertPoly(element, objectStyle, id, objectName, x, y, width, height, rotation, imported, warnings);
                return;
        }

        if (localName == "image")
        {
            var imageStyle = element.Elements().FirstOrDefault(e => e.Name.LocalName == "imageStyle");
            var fileName = (string?)imageStyle?.Attribute("fileName");

            if (string.IsNullOrWhiteSpace(fileName))
            {
                warnings.Add($"'{objectName}' (image) was not converted — it doesn't name its image file.");
                return;
            }

            imported.Add(new ImportedImageObject(id, x, y, width, height, rotation, false, false, fileName, objectName));
            return;
        }

        var text = element.Elements().FirstOrDefault(e => e.Name.LocalName == "data")?.Value ?? string.Empty;
        var fontInfo = element.Elements().FirstOrDefault(e => e.Name.LocalName == "ptFontInfo");
        var logFont = fontInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "logFont");
        var fontExt = fontInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "fontExt");
        var textAlign = element.Elements().FirstOrDefault(e => e.Name.LocalName == "textAlign");

        var fontFamily = (string?)logFont?.Attribute("name") ?? "Arial";
        var fontWeight = int.TryParse(
            (string?)logFont?.Attribute("weight"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight)
            && weight >= 700
                ? "bold"
                : "normal";
        var fontSize = LbxUnits.ParsePoints((string?)fontExt?.Attribute("size")) ?? 12;
        var fill = (string?)fontExt?.Attribute("textColor") ?? "#000000";
        var align = MapAlign((string?)textAlign?.Attribute("horizontalAlignment"));

        var stringItemCount = element.Elements().Count(e => e.Name.LocalName == "stringItem");

        if (stringItemCount > 1)
        {
            warnings.Add(
                $"'{objectName}' mixes multiple text styles in one box; imported using a single uniform style.");
        }

        // P-touch's "database merge" feature binds a text box to a named field; that's the
        // closest analog to Tapeory's dynamic fields. dbMergeFieldStyleName is the merge field's
        // own name (from the bound data source); objectName is just this text box's design-tool
        // identifier, used here only as a human-readable label.
        var expanded = objectStyle.Descendants().FirstOrDefault(e => e.Name.LocalName == "expanded");
        var mergeFieldName = (string?)expanded?.Attribute("dbMergeFieldStyleName");

        if (!string.IsNullOrWhiteSpace(mergeFieldName))
        {
            if (seenFieldNames.Add(mergeFieldName))
            {
                fields.Add(new TemplateFieldDto(mergeFieldName, objectName, text, true));
            }

            imported.Add(new ImportedDynamicFieldObject(
                id, mergeFieldName, objectName, text, true,
                x, y, width, height, rotation, false, false,
                fontSize, fontFamily, fontWeight, align, fill));
        }
        else
        {
            imported.Add(new ImportedTextObject(
                id, text, x, y, width, height, rotation, false, false,
                fontSize, fontFamily, fontWeight, align, fill));
        }
    }

    /// <summary>P-touch barcode protocols Tapeory can print, by the barcodeStyle/@protocol value.</summary>
    private static readonly Dictionary<string, string> BarcodeProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CODE39"] = "code39",
        ["CODE128"] = "code128",
        ["EAN128"] = "code128",
        ["GS1_128"] = "code128",
        ["ITF25"] = "itf",
        ["ITF"] = "itf",
        ["CODABAR"] = "codabar",
        ["NW7"] = "codabar",
        ["UPCA"] = "upca",
        ["UPC_A"] = "upca",
        ["UPCE"] = "upce",
        ["UPC_E"] = "upce",
        ["EAN13"] = "ean13",
        ["JAN13"] = "ean13",
        ["EAN8"] = "ean8",
        ["JAN8"] = "ean8",
        ["QRCODE"] = "qr",
        ["QR"] = "qr",
        ["MICROQRCODE"] = "qr",
        ["DATAMATRIX"] = "datamatrix",
        ["PDF417"] = "pdf417",
        ["AZTEC"] = "aztec"
    };

    private static void ConvertBarcode(
        XElement element, XElement objectStyle, string id, string objectName,
        decimal x, decimal y, decimal width, decimal height, decimal rotation,
        List<object> imported, List<TemplateFieldDto> fields, HashSet<string> seenFieldNames, List<string> warnings)
    {
        var style = element.Elements().FirstOrDefault(e => e.Name.LocalName == "barcodeStyle");
        var protocol = ((string?)style?.Attribute("protocol") ?? "").Trim();

        if (!BarcodeProtocols.TryGetValue(protocol, out var symbology))
        {
            warnings.Add($"'{objectName}' (barcode) was not converted — the {(protocol.Length > 0 ? protocol : "unknown")} barcode type isn't supported yet.");
            return;
        }

        if (protocol.Equals("EAN128", StringComparison.OrdinalIgnoreCase) || protocol.Equals("GS1_128", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"'{objectName}' is a GS1-128 barcode; it was imported as a plain Code 128.");
        }
        else if (protocol.Equals("MICROQRCODE", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"'{objectName}' is a Micro QR code; it was imported as a regular QR code.");
        }

        var data = element.Elements().FirstOrDefault(e => e.Name.LocalName == "data")?.Value ?? string.Empty;
        var showText = string.Equals((string?)style?.Attribute("humanReadable"), "true", StringComparison.OrdinalIgnoreCase);
        var expanded = objectStyle.Descendants().FirstOrDefault(e => e.Name.LocalName == "expanded");
        var mergeFieldName = ((string?)expanded?.Attribute("dbMergeFieldStyleName"))?.Trim() ?? "";

        if (mergeFieldName.Length > 0 && seenFieldNames.Add(mergeFieldName))
        {
            fields.Add(new TemplateFieldDto(mergeFieldName, objectName, data, true));
        }

        imported.Add(new ImportedBarcodeObject(
            id, x, y, width, height, rotation, false, false, symbology, data, mergeFieldName, showText, "#000000"));
    }

    /// <summary>Rectangles, rounded rectangles, ellipses and frames. P-touch keeps the outline in
    /// pt:pen and the fill in pt:brush; a NULL style means none.</summary>
    private static void ConvertShape(
        XElement element, XElement objectStyle, string id, string objectName, string localName,
        decimal x, decimal y, decimal width, decimal height, decimal rotation,
        List<object> imported, List<string> warnings)
    {
        var (stroke, strokeWidth) = ReadPen(objectStyle);
        var fill = ReadBrush(objectStyle);
        var shapeStyle = element.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Style") && e.Name.LocalName != "objectStyle");
        var shape = ((string?)shapeStyle?.Attribute("shape") ?? "").ToUpperInvariant();

        if (localName == "frame")
        {
            warnings.Add($"'{objectName}' is a decorative frame; it was imported as a simple border.");
            imported.Add(new ImportedRectObject(
                id, x, y, width, height, rotation, false, false, "transparent", stroke,
                strokeWidth > 0 ? strokeWidth : LbxUnits.ParsePointsAsMm("0.5pt") ?? 0.18m, 0));
            return;
        }

        if (localName == "ellipse" || shape.Contains("ELLIPSE") || shape.Contains("CIRCLE") || shape.Contains("OVAL"))
        {
            imported.Add(new ImportedEllipseObject(id, x, y, width, height, rotation, false, false, fill, stroke, strokeWidth));
            return;
        }

        var cornerRadius = shape.Contains("ROUND")
            ? LbxUnits.ParsePointsAsMm((string?)shapeStyle?.Attribute("roundnessX")) ?? Math.Min(width, height) / 4
            : 0;

        imported.Add(new ImportedRectObject(
            id, x, y, width, height, rotation, false, false, fill, stroke, strokeWidth, Math.Min(cornerRadius, Math.Min(width, height) / 2)));
    }

    /// <summary>A line from its lineStyle end points when present, otherwise across its box.</summary>
    private static void ConvertLine(
        XElement element, XElement objectStyle, string id,
        decimal x, decimal y, decimal width, decimal height, decimal rotation, List<object> imported)
    {
        var (stroke, strokeWidth) = ReadPen(objectStyle);
        var lineStyle = element.Elements().FirstOrDefault(e => e.Name.LocalName == "lineStyle");
        var x1 = LbxUnits.ParsePointsAsMm((string?)lineStyle?.Attribute("x1"));
        var y1 = LbxUnits.ParsePointsAsMm((string?)lineStyle?.Attribute("y1"));
        var x2 = LbxUnits.ParsePointsAsMm((string?)lineStyle?.Attribute("x2"));
        var y2 = LbxUnits.ParsePointsAsMm((string?)lineStyle?.Attribute("y2"));

        decimal[] points;

        if (x1 is { } ax && y1 is { } ay && x2 is { } bx && y2 is { } by)
        {
            (x, y) = (ax, ay);
            points = [0, 0, bx - ax, by - ay];
        }
        else if (height <= width / 10)
        {
            points = [0, height / 2, width, height / 2]; // horizontal
        }
        else if (width <= height / 10)
        {
            points = [width / 2, 0, width / 2, height]; // vertical
        }
        else
        {
            points = [0, 0, width, height];
        }

        imported.Add(new ImportedLineObject(
            id, x, y, rotation, false, false, points, stroke, strokeWidth > 0 ? strokeWidth : 0.2m));
    }

    /// <summary>
    /// P-touch Editor saves most drawn shapes as draw:poly, with the kind in polyStyle/@shape and
    /// the corner points, in page coordinates, in polyLinePoints/@points ("200.2pt,32.5pt 300.5pt,
    /// 32.5pt"). Lines and polylines become one line per segment; rectangles and ellipses go
    /// through <see cref="ConvertShape"/>.
    /// </summary>
    private static void ConvertPoly(
        XElement element, XElement objectStyle, string id, string objectName,
        decimal x, decimal y, decimal width, decimal height, decimal rotation,
        List<object> imported, List<string> warnings)
    {
        var polyStyle = element.Elements().FirstOrDefault(e => e.Name.LocalName == "polyStyle");
        var shape = ((string?)polyStyle?.Attribute("shape") ?? "").ToUpperInvariant();

        if (shape.Contains("RECT") || shape.Contains("ELLIPSE") || shape.Contains("CIRCLE") || shape.Contains("OVAL"))
        {
            // ConvertShape reads the shape from the first *Style child, which here is polyStyle.
            ConvertShape(element, objectStyle, id, objectName, shape.Contains("RECT") ? "rect" : "ellipse",
                x, y, width, height, rotation, imported, warnings);
            return;
        }

        var points = ParsePolyPoints((string?)polyStyle?.Descendants().FirstOrDefault(e => e.Name.LocalName == "polyLinePoints")?.Attribute("points"));

        if (shape is not ("LINE" or "POLYLINE" or "FREELINE" or "") || points.Count < 2)
        {
            warnings.Add($"'{objectName}' ({(shape.Length > 0 ? shape.ToLowerInvariant() : "poly")}) was not converted — free-form shapes aren't supported yet.");
            return;
        }

        var (stroke, strokeWidth) = ReadPen(objectStyle);

        for (var i = 1; i < points.Count; i++)
        {
            var (fromX, fromY) = points[i - 1];
            var (toX, toY) = points[i];
            imported.Add(new ImportedLineObject(
                i == 1 ? id : $"{id}-{i}", fromX, fromY, rotation, false, false,
                [0, 0, toX - fromX, toY - fromY], stroke, strokeWidth > 0 ? strokeWidth : 0.2m));
        }
    }

    private static List<(decimal X, decimal Y)> ParsePolyPoints(string? points)
    {
        var result = new List<(decimal X, decimal Y)>();

        foreach (var pair in (points ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(',');

            if (parts.Length == 2
                && LbxUnits.ParsePointsAsMm(parts[0]) is { } px
                && LbxUnits.ParsePointsAsMm(parts[1]) is { } py)
            {
                result.Add((px, py));
            }
        }

        return result;
    }

    private static (string Color, decimal Width) ReadPen(XElement objectStyle)
    {
        var pen = objectStyle.Elements().FirstOrDefault(e => e.Name.LocalName == "pen");
        var style = (string?)pen?.Attribute("style");

        if (pen is null || string.Equals(style, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return ("#000000", 0);
        }

        var width = LbxUnits.ParsePointsAsMm((string?)pen.Attribute("widthX"))
                    ?? LbxUnits.ParsePointsAsMm((string?)pen.Attribute("width"))
                    ?? 0.18m;
        return ((string?)pen.Attribute("color") ?? "#000000", width);
    }

    private static string ReadBrush(XElement objectStyle)
    {
        var brush = objectStyle.Elements().FirstOrDefault(e => e.Name.LocalName == "brush");
        var style = (string?)brush?.Attribute("style");

        return brush is null || string.Equals(style, "NULL", StringComparison.OrdinalIgnoreCase)
            ? "transparent"
            : (string?)brush.Attribute("color") ?? "#000000";
    }

    private static string? ReadObjectName(XElement? objectStyle) =>
        (string?)objectStyle?.Descendants().FirstOrDefault(e => e.Name.LocalName == "expanded")?.Attribute("objectName");

    private static string UnsupportedReason(string localName) => localName switch
    {
        "polyLine" or "polygon" => "free-form shapes aren't supported yet.",
        _ => "this object type isn't supported yet."
    };

    private static string MapAlign(string? value) => value?.ToUpperInvariant() switch
    {
        "CENTER" => "center",
        "RIGHT" => "right",
        _ => "left"
    };
}
