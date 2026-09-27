using SkiaSharp;
using Tapeory.Api.Rendering;

namespace Tapeory.Api.Printing;

/// <summary>
/// The label behind a printer's "Test print" button: one line of text, as large as fits, centred
/// both ways on the part of the tape the print head can reach.
/// </summary>
public static class TestLabel
{
    public const string Text = "Tapeory Test Print";

    private const string FontFamily = "Arial";
    private const float MmPerInch = 25.4f;
    private const float PtPerMm = 72f / MmPerInch;

    /// <summary>Space kept clear between the text and the label's ends, and above and below it.</summary>
    private const float EndPaddingMm = 1f;
    private const float EdgePaddingMm = 0.3f;

    public static RenderableDocument Build(decimal widthMm, decimal heightMm, BrotherModel model)
    {
        // The head only reaches the middle of the tape (7 mm of 9 mm tape, for example), so the
        // text has to fit there rather than in the label's full height.
        var (media, _) = BrotherCatalog.MediaForLabel(model, widthMm, heightMm);
        var printableMm = media.PrintPins / (float)model.Dpi * MmPerInch;
        var boxHeight = Math.Max(Math.Min((float)heightMm, printableMm) - 2 * EdgePaddingMm, 1f);
        var boxWidth = Math.Max((float)widthMm - 2 * EndPaddingMm, 1f);

        // The renderer puts text at the top of its box, so size the font to fill the box's
        // height, or its width if the text is too long for that, and then shrink the box to the
        // line to centre it.
        using var typeface = FontResolver.Typeface(FontFamily, bold: false);
        using var font = new SKFont(typeface, 10f);
        var widthAt10Mm = font.MeasureText(Text);
        var fontSizeMm = Math.Min(boxHeight / TextFitter.LineHeightFactor, boxWidth * 10f / widthAt10Mm);
        var lineHeight = fontSizeMm * TextFitter.LineHeightFactor;

        return new RenderableDocument(
            widthMm,
            heightMm,
            [
                new RenderableText(
                    (decimal)EndPaddingMm, ((decimal)heightMm - (decimal)lineHeight) / 2, 0, false, false,
                    Text, (decimal)boxWidth, (decimal)lineHeight,
                    (decimal)(fontSizeMm * PtPerMm), FontFamily, "normal", "center", "#000000",
                    TextFitMode.Shrink)
            ]);
    }
}
