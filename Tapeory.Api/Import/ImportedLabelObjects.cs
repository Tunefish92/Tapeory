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
