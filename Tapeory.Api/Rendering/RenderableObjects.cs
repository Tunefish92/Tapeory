namespace Tapeory.Api.Rendering;

public sealed record RenderableDocument(decimal WidthMm, decimal HeightMm, List<RenderableObject> Objects);

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
