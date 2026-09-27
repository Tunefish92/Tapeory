using SkiaSharp;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printing;

/// <summary>
/// Turns rendered labels into one Brother PT raster print job, following Brother's
/// "PT-E550W/P750W/P710BT Raster Command Reference".
///
/// Every job starts by clearing whatever the printer may still be waiting for (the "invalidate"
/// bytes plus ESC @). Without that, a job that was cut off, or data the printer didn't
/// understand, leaves it mid-command and it silently swallows the jobs that follow.
///
/// The bitmaps are rendered at the job's resolution (180 dpi across the tape, 180 or 360 dpi along
/// it) with the label's length running left to right and the tape width top to bottom. Each pixel
/// column becomes one raster line across the print head; the label is centred on the tape, so rows
/// that fall on the tape's unprintable edges are dropped.
/// </summary>
public static class BrotherPtRasterEncoder
{
    private const int BytesPerLine = BrotherPtTape.HeadPins / 8;

    /// <summary>Feed before and after each label, in dots at 180 dpi: 14 dots (about 2 mm) is
    /// the minimum the PT-P750W accepts.</summary>
    private const int MarginDots = 14;

    public static byte[] Encode(
        IReadOnlyList<SKBitmap> labels,
        BrotherPtTape tape,
        bool highResolution = false,
        CutMode cutMode = CutMode.AutoCut)
    {
        ArgumentOutOfRangeException.ThrowIfZero(labels.Count);

        // ESC i A: full cut after every n labels. Half cut and cut-at-end leave the labels
        // joined and cut once after the last (the command takes at most 255).
        var cutEvery = cutMode is CutMode.HalfCut or CutMode.CutAtEnd ? (byte)Math.Min(labels.Count, 255) : (byte)1;

        // ESC i K: 0x08 feeds and cuts the last label (chain printing leaves it in the printer),
        // 0x04 half-cuts between labels, 0x40 prints at 180 × 360 dpi.
        var advancedMode = (byte)((cutMode == CutMode.ChainPrinting ? 0x00 : 0x08)
                                  | (cutMode == CutMode.HalfCut ? 0x04 : 0x00)
                                  | (highResolution ? 0x40 : 0x00));

        using var output = new MemoryStream();

        output.Write(new byte[100]);            // invalidate
        output.Write([0x1B, 0x40]);             // ESC @: initialize
        output.Write([0x1B, 0x69, 0x61, 0x01]); // ESC i a: raster mode

        for (var page = 0; page < labels.Count; page++)
        {
            var label = labels[page];
            var lines = label.Width;

            // ESC i z: print information. Flags: 0x80 printer recovery on, 0x04 tape width valid.
            output.Write([0x1B, 0x69, 0x7A, 0x84, 0x00, tape.WidthCode, 0x00]);
            output.Write(BitConverter.GetBytes(lines)); // raster line count, little-endian
            output.Write([page == 0 ? (byte)0 : (byte)1, 0x00]);

            output.Write([0x1B, 0x69, 0x4D, 0x40]); // ESC i M: auto cut on
            output.Write([0x1B, 0x69, 0x41, cutEvery]);
            output.Write([0x1B, 0x69, 0x4B, advancedMode]);
            var margin = highResolution ? MarginDots * 2 : MarginDots; // margin is counted in raster lines
            output.Write([0x1B, 0x69, 0x64, (byte)(margin & 0xFF), (byte)(margin >> 8)]); // ESC i d: margin
            output.Write([0x4D, 0x02]);             // M: TIFF (PackBits) compression

            WriteRasterLines(output, label, tape);

            output.WriteByte(page == labels.Count - 1 ? (byte)0x1A : (byte)0x0C); // print (last page: and feed)
        }

        return output.ToArray();
    }

    private static void WriteRasterLines(Stream output, SKBitmap label, BrotherPtTape tape)
    {
        // Label rows that land on the printable pins, centring the label on the tape.
        var firstRow = (label.Height - tape.PrintPins) / 2;
        var line = new byte[BytesPerLine];

        for (var x = 0; x < label.Width; x++)
        {
            Array.Clear(line);
            var anyDot = false;

            for (var pin = 0; pin < tape.PrintPins; pin++)
            {
                var y = firstRow + pin;

                if (y < 0 || y >= label.Height || !IsDark(label.GetPixel(x, y)))
                {
                    continue;
                }

                var bit = tape.MarginPins + pin;
                line[bit / 8] |= (byte)(0x80 >> (bit % 8));
                anyDot = true;
            }

            if (!anyDot)
            {
                output.WriteByte(0x5A); // Z: empty raster line
                continue;
            }

            // G: one raster line, sent as a single PackBits literal run of all 16 bytes.
            output.Write([0x47, BytesPerLine + 1, 0x00, BytesPerLine - 1]);
            output.Write(line);
        }
    }

    /// <summary>Thresholds at mid-grey, treating transparency as white.</summary>
    private static bool IsDark(SKColor color)
    {
        var luminance = (0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue) / 255f;
        var alpha = color.Alpha / 255f;
        return luminance * alpha + (1 - alpha) < 0.5f;
    }
}
