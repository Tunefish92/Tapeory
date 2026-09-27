using SkiaSharp;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printing;

/// <summary>
/// Turns rendered labels into one Brother raster print job, for P-touch (PT) and QL printers,
/// following Brother's Raster Command References.
///
/// Every job starts by clearing whatever the printer may still be waiting for (the "invalidate"
/// NUL bytes plus ESC @). Without that, a job that was cut off, or data the printer didn't
/// understand, leaves it mid-command and it silently swallows the jobs that follow.
///
/// The bitmaps are rendered at the job's resolution with the label's length running left to
/// right (the feed direction) and the tape or roll width top to bottom. Each pixel column becomes
/// one raster line across the print head: the label is centred on the media, so rows that fall
/// on its unprintable edges are dropped, and die-cut labels are trimmed to their printable length.
/// </summary>
public static class BrotherRasterEncoder
{
    /// <summary>Feed before and after each P-touch label, in dots at the head's dpi: 2 mm, above
    /// the minimum every model accepts (14 dots at 180 dpi, 14 at 360 dpi).</summary>
    private static int PtMarginDots(BrotherModel model) => model.Family == BrotherFamily.Pt360 ? 28 : 14;

    /// <summary>Feed around each QL label on a continuous roll: 3 mm (Brother's default, 35 dots).</summary>
    private const int QlContinuousMarginDots = 35;

    public static byte[] Encode(
        IReadOnlyList<SKBitmap> labels,
        BrotherModel model,
        BrotherMedia media,
        bool highResolution = false,
        CutMode cutMode = CutMode.AutoCut)
    {
        ArgumentOutOfRangeException.ThrowIfZero(labels.Count);

        return model.IsQl
            ? EncodeQl(labels, model, media, highResolution && model.HighResolution, cutMode)
            : EncodePt(labels, model, media, highResolution && model.HighResolution, cutMode);
    }

    private static byte[] EncodePt(
        IReadOnlyList<SKBitmap> labels, BrotherModel model, BrotherMedia media, bool highResolution, CutMode cutMode)
    {
        var bytesPerLine = model.HeadPins / 8;
        // Margin and gaps are counted in raster lines, which are twice as dense in high resolution.
        var marginLines = PtMarginDots(model) * (highResolution ? 2 : 1);
        var labelLines = labels.Select(label => RasterLines(label, media, bytesPerLine, twoColor: false).Black).ToList();

        // Cut marks: no cutting at all; the labels print as one strip with a dashed line across
        // the tape at each cut, centred in the gap, to cut along by hand.
        var pages = cutMode == CutMode.CutMarks
            ? [WithCutMarks(labelLines, CutMarkLine(media, bytesPerLine), marginLines)]
            : labelLines;

        // ESC i A: full cut after every n labels. Half cut and cut-at-end leave the labels
        // joined and cut once after the last (the command takes at most 255).
        var cutEvery = cutMode is CutMode.HalfCut or CutMode.CutAtEnd ? (byte)Math.Min(labels.Count, 255) : (byte)1;

        // ESC i K: 0x08 feeds and cuts the last label (chain printing leaves it in the printer),
        // 0x04 half-cuts between labels, 0x40 doubles the resolution along the tape.
        var advancedMode = (byte)((cutMode == CutMode.ChainPrinting ? 0x00 : 0x08)
                                  | (cutMode == CutMode.HalfCut && model.HalfCut ? 0x04 : 0x00)
                                  | (highResolution ? 0x40 : 0x00));

        // ESC i z flags: 0x80 printer recovery on, 0x04 tape width valid (so the printer checks the
        // loaded tape). Heat-shrink tube widths don't have a whole-mm code, so tubes skip the check.
        var validFlags = (byte)(media.Kind == MediaKind.Tube ? 0x80 : 0x84);

        using var output = new MemoryStream();

        output.Write(new byte[model.InvalidateBytes]); // invalidate
        output.Write([0x1B, 0x40]);                    // ESC @: initialize
        output.Write([0x1B, 0x69, 0x61, 0x01]);        // ESC i a: raster mode

        for (var page = 0; page < pages.Count; page++)
        {
            var lines = pages[page];

            output.Write([0x1B, 0x69, 0x7A, validFlags, 0x00, media.WidthCode, 0x00]); // ESC i z: print information
            output.Write(BitConverter.GetBytes(lines.Count)); // raster line count, little-endian
            output.Write([page == 0 ? (byte)0 : (byte)1, 0x00]);

            output.Write([0x1B, 0x69, 0x4D, cutMode == CutMode.CutMarks ? (byte)0x00 : (byte)0x40]); // ESC i M: auto cut
            output.Write([0x1B, 0x69, 0x41, cutEvery]);
            output.Write([0x1B, 0x69, 0x4B, advancedMode]);
            output.Write([0x1B, 0x69, 0x64, (byte)(marginLines & 0xFF), (byte)(marginLines >> 8)]); // ESC i d: margin
            output.Write([0x4D, 0x02]); // M: TIFF (PackBits) compression

            foreach (var line in lines)
            {
                if (line is null)
                {
                    output.WriteByte(0x5A); // Z: empty raster line
                    continue;
                }

                // G: one raster line, sent as PackBits literal runs (at most 128 bytes each).
                var packed = PackBitsLiteral(line);
                output.Write([0x47, (byte)(packed.Length & 0xFF), (byte)(packed.Length >> 8)]);
                output.Write(packed);
            }

            output.WriteByte(page == pages.Count - 1 ? (byte)0x1A : (byte)0x0C); // print (last page: and feed)
        }

        return output.ToArray();
    }

    private static byte[] EncodeQl(
        IReadOnlyList<SKBitmap> labels, BrotherModel model, BrotherMedia media, bool highResolution, CutMode cutMode)
    {
        var bytesPerLine = model.HeadPins / 8;
        var twoColor = media.TwoColor && model.TwoColor;
        var cut = model.AutoCut && cutMode != CutMode.CutMarks;
        var marginLines = media.IsDieCut ? 0 : QlContinuousMarginDots * (highResolution ? 2 : 1);

        var rasters = labels.Select(label =>
        {
            // A die-cut label's raster line count must equal its printable length.
            var trimmed = media.IsDieCut ? TrimToLength(label, media.PrintLengthDots * (highResolution ? 2 : 1)) : label;
            try
            {
                return RasterLines(trimmed, media, bytesPerLine, twoColor);
            }
            finally
            {
                if (!ReferenceEquals(trimmed, label))
                {
                    trimmed.Dispose();
                }
            }
        }).ToList();

        // Cut marks on a continuous roll: one uncut strip with dashed lines between the labels.
        // Die-cut labels are already separate, so they're just not cut.
        if (cutMode == CutMode.CutMarks && !media.IsDieCut)
        {
            var mark = CutMarkLine(media, bytesPerLine);
            var black = WithCutMarks([.. rasters.Select(r => r.Black)], mark, marginLines);
            var red = twoColor ? WithCutMarks([.. rasters.Select(r => r.Red!)], null, marginLines) : null;
            rasters = [(black, red)];
        }

        var cutEvery = cutMode == CutMode.CutAtEnd ? (byte)Math.Min(labels.Count, 255) : (byte)1;

        using var output = new MemoryStream();

        output.Write(new byte[model.InvalidateBytes]); // invalidate
        output.Write([0x1B, 0x40]);                    // ESC @: initialize

        if (model.ModeSwitch)
        {
            output.Write([0x1B, 0x69, 0x61, 0x01]); // ESC i a: raster mode
        }

        for (var page = 0; page < rasters.Count; page++)
        {
            var (black, red) = rasters[page];

            // ESC i z: print information. Flags: 0x80 recovery, 0x40 quality, 0x08 length, 0x04
            // width and 0x02 media type valid. Media type 0x0A continuous roll, 0x0B die-cut.
            output.Write([
                0x1B, 0x69, 0x7A, 0xCE,
                media.IsDieCut ? (byte)0x0B : (byte)0x0A,
                media.WidthCode,
                media.IsDieCut ? (byte)Math.Min(media.LengthMm, 255) : (byte)0
            ]);
            output.Write(BitConverter.GetBytes(black.Count));
            output.Write([page == 0 ? (byte)0 : (byte)1, 0x00]);

            if (model.AutoCut)
            {
                output.Write([0x1B, 0x69, 0x4D, cut ? (byte)0x40 : (byte)0x00]); // ESC i M: auto cut

                if (cut && model.CutEvery)
                {
                    output.Write([0x1B, 0x69, 0x41, cutEvery]); // ESC i A: cut every n labels
                }
            }

            if (model.ExpandedMode)
            {
                // ESC i K: 0x08 cut at the end, 0x40 600 dpi along the feed, 0x01 two-colour printing.
                output.Write([0x1B, 0x69, 0x4B, (byte)((cut ? 0x08 : 0x00) | (highResolution ? 0x40 : 0x00) | (twoColor ? 0x01 : 0x00))]);
            }

            output.Write([0x1B, 0x69, 0x64, (byte)(marginLines & 0xFF), (byte)(marginLines >> 8)]); // ESC i d: margin

            if (model.Compression)
            {
                output.Write([0x4D, 0x00]); // M: no compression; lines are sent whole
            }

            for (var i = 0; i < black.Count; i++)
            {
                if (twoColor)
                {
                    output.Write([0x77, 0x01, (byte)bytesPerLine]); // w: black plane
                    output.Write(black[i] ?? new byte[bytesPerLine]);
                    output.Write([0x77, 0x02, (byte)bytesPerLine]); // w: red plane
                    output.Write(red![i] ?? new byte[bytesPerLine]);
                }
                else
                {
                    output.Write([0x67, 0x00, (byte)bytesPerLine]); // g: one raster line
                    output.Write(black[i] ?? new byte[bytesPerLine]);
                }
            }

            output.WriteByte(page == rasters.Count - 1 ? (byte)0x1A : (byte)0x0C); // print (last page: and feed)
        }

        return output.ToArray();
    }

    /// <summary>
    /// One label's raster lines, one per pixel column, null for a line without dots. The label's
    /// top row lands on the first printable pin: <see cref="BrotherMedia.OffsetPins"/> into the
    /// raster line. For two-colour media, red pixels go to their own plane.
    /// </summary>
    private static (List<byte[]?> Black, List<byte[]?>? Red) RasterLines(
        SKBitmap label, BrotherMedia media, int bytesPerLine, bool twoColor)
    {
        // Label rows that land on the printable pins, centring the label on the media.
        var firstRow = (label.Height - media.PrintPins) / 2;
        var black = new List<byte[]?>(label.Width);
        var red = twoColor ? new List<byte[]?>(label.Width) : null;

        for (var x = 0; x < label.Width; x++)
        {
            byte[]? blackLine = null;
            byte[]? redLine = null;

            for (var pin = 0; pin < media.PrintPins; pin++)
            {
                var y = firstRow + pin;

                if (y < 0 || y >= label.Height)
                {
                    continue;
                }

                var color = label.GetPixel(x, y);

                if (twoColor && IsRed(color))
                {
                    SetPin(redLine ??= new byte[bytesPerLine], media.OffsetPins + pin);
                }
                else if (IsDark(color))
                {
                    SetPin(blackLine ??= new byte[bytesPerLine], media.OffsetPins + pin);
                }
            }

            black.Add(blackLine);
            red?.Add(redLine);
        }

        return (black, red);
    }

    /// <summary>Crops (or pads with white) a die-cut label's length to the printable dots,
    /// keeping it centred.</summary>
    private static SKBitmap TrimToLength(SKBitmap label, int length)
    {
        if (label.Width == length)
        {
            return label;
        }

        var trimmed = new SKBitmap(length, label.Height);
        using var canvas = new SKCanvas(trimmed);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(
            label,
            SKRect.Create((length - label.Width) / 2f, 0, label.Width, label.Height),
            new SKSamplingOptions(SKFilterMode.Nearest));
        return trimmed;
    }

    /// <summary>All labels on one strip: a cut mark before the first, between each pair, and after
    /// the last, with a gap either side of each mark so labels don't touch it.</summary>
    private static List<byte[]?> WithCutMarks(List<List<byte[]?>> labels, byte[]? mark, int gap)
    {
        var strip = new List<byte[]?> { mark };

        foreach (var label in labels)
        {
            strip.AddRange(Enumerable.Repeat<byte[]?>(null, gap));
            strip.AddRange(label);
            strip.AddRange(Enumerable.Repeat<byte[]?>(null, gap));
            strip.Add(mark);
        }

        return strip;
    }

    /// <summary>A dashed line across the printable width of the media.</summary>
    private static byte[] CutMarkLine(BrotherMedia media, int bytesPerLine)
    {
        var line = new byte[bytesPerLine];

        for (var pin = 0; pin < media.PrintPins; pin++)
        {
            if (pin / 3 % 2 == 0)
            {
                SetPin(line, media.OffsetPins + pin);
            }
        }

        return line;
    }

    private static void SetPin(byte[] line, int bit) => line[bit / 8] |= (byte)(0x80 >> (bit % 8));

    /// <summary>A PackBits stream of literal runs only (header n = run length − 1, 0..127).</summary>
    private static byte[] PackBitsLiteral(byte[] line)
    {
        var packed = new List<byte>(line.Length + line.Length / 128 + 1);

        for (var start = 0; start < line.Length; start += 128)
        {
            var count = Math.Min(128, line.Length - start);
            packed.Add((byte)(count - 1));
            packed.AddRange(line.AsSpan(start, count).ToArray());
        }

        return [.. packed];
    }

    /// <summary>Thresholds at mid-grey, treating transparency as white.</summary>
    private static bool IsDark(SKColor color)
    {
        var luminance = (0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue) / 255f;
        var alpha = color.Alpha / 255f;
        return luminance * alpha + (1 - alpha) < 0.5f;
    }

    /// <summary>Clearly red (the printer's second colour), not dark red-brown or pink-grey.</summary>
    private static bool IsRed(SKColor color) =>
        color.Alpha > 127 && color.Red > 150 && color.Green < 110 && color.Blue < 110;
}
