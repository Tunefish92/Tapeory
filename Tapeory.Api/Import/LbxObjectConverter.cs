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
/// Text objects (text:text) and images (image:image) are converted. A text object bound to a
/// P-touch "database merge" field (pt:expanded/@dbMergeFieldStyleName is non-empty) becomes a
/// Tapeory dynamic field instead of fixed text — this is the "named object" -> dynamic field
/// mapping described in the project plan. An image keeps its position and size and points at
/// its file in the archive (imageStyle/@fileName, a BMP or TIFF); LbxImportService converts and
/// stores that file. Barcodes and draw:* shapes are deliberately left unconverted with an
/// explicit warning rather than guessed at, per the plan's "partial conversion with explicit
/// warnings instead of silently discarding" approach.
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

        if (localName is not ("text" or "image"))
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

        if (localName == "image")
        {
            var imageStyle = element.Elements().FirstOrDefault(e => e.Name.LocalName == "imageStyle");
            var fileName = (string?)imageStyle?.Attribute("fileName");

            if (string.IsNullOrWhiteSpace(fileName))
            {
                warnings.Add($"'{objectName}' (image) was not converted — it doesn't name its image file.");
                return;
            }

            imported.Add(new ImportedImageObject(
                $"lbx-{index}-{Guid.NewGuid():N}", x, y, width, height, rotation, false, false, fileName, objectName));
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
        var id = $"lbx-{index}-{Guid.NewGuid():N}";

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

    private static string? ReadObjectName(XElement? objectStyle) =>
        (string?)objectStyle?.Descendants().FirstOrDefault(e => e.Name.LocalName == "expanded")?.Attribute("objectName");

    private static string UnsupportedReason(string localName) => localName switch
    {
        "barcode" => "barcode objects aren't supported by the editor yet.",
        _ => "this object type isn't supported yet."
    };

    private static string MapAlign(string? value) => value?.ToUpperInvariant() switch
    {
        "CENTER" => "center",
        "RIGHT" => "right",
        _ => "left"
    };
}
