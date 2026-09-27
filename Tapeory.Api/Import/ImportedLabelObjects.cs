using System.Text.Json.Serialization;

namespace Tapeory.Api.Import;

// Mirrors the shapes in Tapeory.Web's src/editor/types.ts (TextObject / DynamicFieldObject /
// ImageObject).
// Serialized with the API's usual camelCase JSON options so the resulting editorJson loads
// straight into the browser editor. See LbxObjectConverter for which source object types are
// supported.

public sealed record ImportedLabelDocument(int FormatVersion, decimal WidthMm, decimal HeightMm, List<object> Objects);

public sealed record ImportedTextObject(
    string Id,
    string Text,
    decimal X,
    decimal Y,
    decimal Width,
    decimal Height,
    decimal Rotation,
    bool Locked,
    bool Hidden,
    decimal FontSize,
    string FontFamily,
    string FontWeight,
    string Align,
    string Fill)
{
    public string Type => "text";
}

public sealed record ImportedDynamicFieldObject(
    string Id,
    string FieldName,
    string Label,
    string DefaultValue,
    bool Required,
    decimal X,
    decimal Y,
    decimal Width,
    decimal Height,
    decimal Rotation,
    bool Locked,
    bool Hidden,
    decimal FontSize,
    string FontFamily,
    string FontWeight,
    string Align,
    string Fill)
{
    public string Type => "dynamicField";
}

/// <summary>An embedded image. The converter only knows which archive file it came from;
/// LbxImportService stores that file as an upload and fills in UploadedFileId and Url.</summary>
public sealed record ImportedImageObject(
    string Id,
    decimal X,
    decimal Y,
    decimal Width,
    decimal Height,
    decimal Rotation,
    bool Locked,
    bool Hidden,
    [property: JsonIgnore] string SourceFileName,
    [property: JsonIgnore] string Name)
{
    public string Type => "image";

    public int UploadedFileId { get; set; }

    public string Url { get; set; } = "";
}

public sealed record ImportedRectObject(
    string Id, decimal X, decimal Y, decimal Width, decimal Height, decimal Rotation, bool Locked, bool Hidden,
    string Fill, string Stroke, decimal StrokeWidth, decimal CornerRadius)
{
    public string Type => "rect";
}

public sealed record ImportedEllipseObject(
    string Id, decimal X, decimal Y, decimal Width, decimal Height, decimal Rotation, bool Locked, bool Hidden,
    string Fill, string Stroke, decimal StrokeWidth)
{
    public string Type => "ellipse";
}

/// <param name="Points">Start and end, relative to X/Y.</param>
public sealed record ImportedLineObject(
    string Id, decimal X, decimal Y, decimal Rotation, bool Locked, bool Hidden,
    decimal[] Points, string Stroke, decimal StrokeWidth)
{
    public string Type => "line";
}

public sealed record ImportedBarcodeObject(
    string Id, decimal X, decimal Y, decimal Width, decimal Height, decimal Rotation, bool Locked, bool Hidden,
    string Symbology, string Data, string FieldName, bool ShowText, string Fill)
{
    public string Type => "barcode";
}
