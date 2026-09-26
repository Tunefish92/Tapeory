namespace Tapeory.Api.Import;

// Mirrors the shapes in Tapeory.Web's src/editor/types.ts (TextObject / DynamicFieldObject).
// Serialized with the API's usual camelCase JSON options so the resulting editorJson loads
// straight into the browser editor. Only these two object types are produced by the importer
// today — see LbxObjectConverter for which source object types are supported.

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
