namespace Tapeory.Api.Rendering;

/// <param name="Media">The Brother medium chosen in the editor (e.g. "tze-9", "DK-22251"), if any.</param>
public sealed record RenderableDocument(decimal WidthMm, decimal HeightMm, List<RenderableObject> Objects, string? Media = null);

public abstract record RenderableObject(decimal X, decimal Y, decimal Rotation, bool Locked, bool Hidden);

public sealed record RenderableText(
    decimal X, decimal Y, decimal Rotation, bool Locked, bool Hidden,
    string Text, decimal Width, decimal Height, decimal FontSize, string FontFamily,
    string FontWeight, string Align, string Fill, TextFitMode Fit = TextFitMode.None)
    : RenderableObject(X, Y, Rotation, Locked, Hidden);

public sealed record RenderableDynamicField(
    decimal X, decimal Y, decimal Rotation, bool Locked, bool Hidden,
    string FieldName, decimal Width, decimal Height, decimal FontSize, string FontFamily,
    string FontWeight, string Align, string Fill, TextFitMode Fit = TextFitMode.None)
    : RenderableObject(X, Y, Rotation, Locked, Hidden);

public sealed record RenderableRect(
    decimal X, decimal Y, decimal Rotation, bool Locked, bool Hidden,
    decimal Width, decimal Height, string Fill, string Stroke, decimal StrokeWidth, decimal CornerRadius)
    : RenderableObject(X, Y, Rotation, Locked, Hidden);

public sealed record RenderableLine(
    decimal X, decimal Y, decimal Rotation, bool Locked, bool Hidden,
    decimal[] Points, string Stroke, decimal StrokeWidth)
    : RenderableObject(X, Y, Rotation, Locked, Hidden);

public sealed record RenderableImage(
    decimal X, decimal Y, decimal Rotation, bool Locked, bool Hidden,
    decimal Width, decimal Height, int UploadedFileId, string Url)
    : RenderableObject(X, Y, Rotation, Locked, Hidden);

public sealed record RenderableEllipse(
    decimal X, decimal Y, decimal Rotation, bool Locked, bool Hidden,
    decimal Width, decimal Height, string Fill, string Stroke, decimal StrokeWidth)
    : RenderableObject(X, Y, Rotation, Locked, Hidden);

/// <param name="FieldName">When set, the value comes from this template field at print time,
/// with <paramref name="Data"/> as the fallback; otherwise <paramref name="Data"/> is printed.</param>
/// <param name="ShowText">For 1D barcodes: print the value in plain text under the bars.</param>
public sealed record RenderableBarcode(
    decimal X, decimal Y, decimal Rotation, bool Locked, bool Hidden,
    decimal Width, decimal Height, string Symbology, string Data, string FieldName, bool ShowText, string Fill)
    : RenderableObject(X, Y, Rotation, Locked, Hidden)
{
    public string ValueFor(IReadOnlyDictionary<string, string> fieldValues) =>
        FieldName.Length > 0 && fieldValues.TryGetValue(FieldName, out var value) && value.Length > 0 ? value : Data;
}
