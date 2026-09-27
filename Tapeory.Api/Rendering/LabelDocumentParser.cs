using System.Text.Json;

namespace Tapeory.Api.Rendering;

/// <summary>
/// Parses a template version's editorJson (produced either by the browser editor or the .lbx
/// importer) into a renderable model. Field access is defensive throughout — an unrecognized
/// object type is skipped rather than rejecting the whole document, and a missing/mistyped field
/// falls back to a sensible default — since editorJson is an evolving format shared between the
/// frontend and backend, and a document shouldn't fail to render just because one field is
/// absent or a future object type isn't known yet.
/// </summary>
public static class LabelDocumentParser
{
    private const decimal DefaultWidthMm = 50m;
    private const decimal DefaultHeightMm = 25m;

    public static RenderableDocument Parse(string editorJson)
    {
        using var document = JsonDocument.Parse(editorJson);
        var root = document.RootElement;

        var widthMm = GetDecimal(root, "widthMm");
        var heightMm = GetDecimal(root, "heightMm");

        if (widthMm <= 0) widthMm = DefaultWidthMm;
        if (heightMm <= 0) heightMm = DefaultHeightMm;

        var objects = new List<RenderableObject>();

        if (root.TryGetProperty("objects", out var objectsElement) && objectsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in objectsElement.EnumerateArray())
            {
                var parsed = ParseObject(element);

                if (parsed is not null)
                {
                    objects.Add(parsed);
                }
            }
        }

        var media = root.TryGetProperty("media", out var mediaElement) && mediaElement.ValueKind == JsonValueKind.String
            ? mediaElement.GetString()
            : null;

        return new RenderableDocument(widthMm, heightMm, objects, media);
    }

    private static RenderableObject? ParseObject(JsonElement element)
    {
        var type = GetString(element, "type");

        var x = GetDecimal(element, "x");
        var y = GetDecimal(element, "y");
        var rotation = GetDecimal(element, "rotation");
        var locked = GetBool(element, "locked");
        var hidden = GetBool(element, "hidden");
        var width = GetDecimal(element, "width");
        var height = GetDecimal(element, "height");
        var fontSize = GetDecimal(element, "fontSize");
        var fontFamily = GetString(element, "fontFamily") ?? "Arial";
        var fontWeight = GetString(element, "fontWeight") ?? "normal";
        var align = GetString(element, "align") ?? "left";
        var fill = GetString(element, "fill") ?? "#000000";
        var stroke = GetString(element, "stroke") ?? "#000000";
        var strokeWidth = GetDecimal(element, "strokeWidth");
        var fit = TextFitter.ParseMode(GetString(element, "fit"));

        return type switch
        {
            "text" => new RenderableText(
                x, y, rotation, locked, hidden,
                GetString(element, "text") ?? string.Empty,
                width, height, fontSize, fontFamily, fontWeight, align, fill, fit),

            "dynamicField" => new RenderableDynamicField(
                x, y, rotation, locked, hidden,
                GetString(element, "fieldName") ?? string.Empty,
                width, height, fontSize, fontFamily, fontWeight, align, fill, fit),

            "rect" => new RenderableRect(
                x, y, rotation, locked, hidden,
                width, height, GetString(element, "fill") ?? "transparent", stroke, strokeWidth,
                GetDecimal(element, "cornerRadius")),

            "line" => new RenderableLine(
                x, y, rotation, locked, hidden,
                GetDecimalArray(element, "points"), stroke, strokeWidth),

            "ellipse" => new RenderableEllipse(
                x, y, rotation, locked, hidden,
                width, height, GetString(element, "fill") ?? "transparent", stroke, strokeWidth),

            "barcode" => new RenderableBarcode(
                x, y, rotation, locked, hidden,
                width, height,
                GetString(element, "symbology") ?? "code128",
                GetString(element, "data") ?? string.Empty,
                GetString(element, "fieldName") ?? string.Empty,
                GetBool(element, "showText"),
                fill),

            "image" => new RenderableImage(
                x, y, rotation, locked, hidden,
                width, height, GetInt(element, "uploadedFileId"), GetString(element, "url") ?? string.Empty),

            // Unrecognized/future object type — skip rather than fail the whole document.
            _ => null
        };
    }

    private static decimal GetDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : 0m;

    private static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static bool GetBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal[] GetDecimalArray(JsonElement element, string property)
    {
        var result = new decimal[4];

        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var index = 0;

        foreach (var item in value.EnumerateArray())
        {
            if (index >= result.Length)
            {
                break;
            }

            result[index] = item.ValueKind == JsonValueKind.Number ? item.GetDecimal() : 0m;
            index++;
        }

        return result;
    }
}
