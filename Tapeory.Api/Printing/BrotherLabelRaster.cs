using SkiaSharp;
using Tapeory.Api.Rendering;

namespace Tapeory.Api.Printing;

/// <summary>Renders a label the way a Brother printer needs it: at the model's resolution, with
/// the tape or roll width top to bottom. Die-cut templates designed the other way round (width
/// across the roll) are turned a quarter clockwise.</summary>
public static class BrotherLabelRaster
{
    public static (SKBitmap Bitmap, BrotherMedia Media) Render(
        LabelRenderer renderer,
        RenderableDocument document,
        IReadOnlyDictionary<string, string> fieldValues,
        ImageResolver resolveImage,
        BrotherModel model,
        PrintResolution resolution)
    {
        var (media, rotated) = BrotherCatalog.MediaForLabel(model, document.WidthMm, document.HeightMm, document.Media);

        if (!rotated)
        {
            return (renderer.RenderBitmap(document, fieldValues, resolveImage, resolution.HorizontalDpi, resolution.VerticalDpi), media);
        }

        // The template's width runs across the roll: render it with the dpi swapped, then turn it.
        using var upright = renderer.RenderBitmap(document, fieldValues, resolveImage, resolution.VerticalDpi, resolution.HorizontalDpi);
        var turned = new SKBitmap(upright.Height, upright.Width);

        using (var canvas = new SKCanvas(turned))
        {
            canvas.Clear(SKColors.White);
            canvas.Translate(turned.Width, 0);
            canvas.RotateDegrees(90);
            canvas.DrawBitmap(upright, SKRect.Create(upright.Width, upright.Height), new SKSamplingOptions(SKFilterMode.Nearest));
        }

        return (turned, media);
    }
}
