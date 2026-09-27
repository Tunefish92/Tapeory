using SkiaSharp;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;

namespace Tapeory.Api.Tests.Unit;

public sealed class BrotherPtRasterEncoderTests
{
    private static readonly BrotherPtTape NineMm = BrotherPtTape.ForLabelHeight(9m);

    private static SKBitmap WhiteLabel(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        return bitmap;
    }

    /// <summary>Everything after the per-page setup commands: the raster lines and the final print byte.</summary>
    private static byte[] RasterSection(byte[] job)
    {
        var compressionAt = IndexOf(job, [0x4D, 0x02]);
        return job[(compressionAt + 2)..];
    }

    private static int IndexOf(byte[] data, byte[] pattern)
    {
        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public void Encode_StartsWithInvalidateInitializeAndRasterMode()
    {
        using var label = WhiteLabel(10, 64);

        var job = BrotherPtRasterEncoder.Encode([label], NineMm);

        Assert.All(job[..100], b => Assert.Equal(0, b));
        Assert.Equal([0x1B, 0x40, 0x1B, 0x69, 0x61, 0x01], job[100..106]);
    }

    [Fact]
    public void Encode_WritesTheTapeWidthAndRasterLineCount_InThePrintInformation()
    {
        using var label = WhiteLabel(300, 64);

        var job = BrotherPtRasterEncoder.Encode([label], NineMm);
        var info = IndexOf(job, [0x1B, 0x69, 0x7A]);

        Assert.Equal(9, job[info + 5]);
        Assert.Equal(300, BitConverter.ToInt32(job, info + 7));
    }

    [Fact]
    public void Encode_SendsBlankColumnsAsEmptyLines_AndEndsWithPrintAndFeed()
    {
        using var label = WhiteLabel(3, 64);

        var raster = RasterSection(BrotherPtRasterEncoder.Encode([label], NineMm));

        Assert.Equal([0x5A, 0x5A, 0x5A, 0x1A], raster);
    }

    [Fact]
    public void Encode_MapsTheTopPrintableRowToTheFirstPinAfterTheTapeMargin()
    {
        // 64 rows on 9 mm tape (50 printable pins): rows 7..56 print, so row 7 is the first pin,
        // which sits after the 39-pin margin.
        using var label = WhiteLabel(1, 64);
        label.SetPixel(0, 7, SKColors.Black);

        var raster = RasterSection(BrotherPtRasterEncoder.Encode([label], NineMm));

        Assert.Equal([0x47, 17, 0x00, 15], raster[..4]);
        var line = raster[4..20];
        Assert.Equal(0x80 >> (39 % 8), line[39 / 8]);
        Assert.Equal(15, line.Count(b => b == 0));
    }

    [Fact]
    public void Encode_DropsRowsOnTheTapesUnprintableEdges()
    {
        using var label = WhiteLabel(1, 64);
        label.SetPixel(0, 0, SKColors.Black);
        label.SetPixel(0, 63, SKColors.Black);

        var raster = RasterSection(BrotherPtRasterEncoder.Encode([label], NineMm));

        Assert.Equal([0x5A, 0x1A], raster);
    }

    [Fact]
    public void Encode_SeparatesCopiesWithFormFeed_AndMarksLaterPagesAsNotFirst()
    {
        using var label = WhiteLabel(2, 64);

        var job = BrotherPtRasterEncoder.Encode([label, label, label], NineMm);

        var firstInfo = IndexOf(job, [0x1B, 0x69, 0x7A]);
        var secondInfo = IndexOf(job[(firstInfo + 1)..], [0x1B, 0x69, 0x7A]) + firstInfo + 1;
        Assert.Equal(0, job[firstInfo + 11]);
        Assert.Equal(1, job[secondInfo + 11]);
        Assert.Equal(2, job.Count(b => b == 0x0C));
        Assert.Equal(0x1A, job[^1]);
    }

    [Theory]
    [InlineData(false, 0x08, 14)]
    [InlineData(true, 0x48, 28)]
    public void Encode_SetsHighResolutionAndDoublesTheMargin_WhenAsked(bool highResolution, byte mode, byte margin)
    {
        using var label = WhiteLabel(2, 64);

        var job = BrotherPtRasterEncoder.Encode([label], NineMm, highResolution);

        Assert.Equal(mode, job[IndexOf(job, [0x1B, 0x69, 0x4B]) + 3]);
        Assert.Equal(margin, job[IndexOf(job, [0x1B, 0x69, 0x64]) + 3]);
    }

    [Theory]
    [InlineData(CutMode.AutoCut, 1, 0x08)]
    [InlineData(CutMode.HalfCut, 3, 0x0C)]
    [InlineData(CutMode.CutAtEnd, 3, 0x08)]
    [InlineData(CutMode.ChainPrinting, 1, 0x00)]
    public void Encode_SetsTheCutCommands_ForEachCutMode(CutMode cutMode, byte cutEvery, byte mode)
    {
        using var label = WhiteLabel(2, 64);

        var job = BrotherPtRasterEncoder.Encode([label, label, label], NineMm, cutMode: cutMode);

        Assert.Equal(0x40, job[IndexOf(job, [0x1B, 0x69, 0x4D]) + 3]); // auto cut on
        Assert.Equal(cutEvery, job[IndexOf(job, [0x1B, 0x69, 0x41]) + 3]);
        Assert.Equal(mode, job[IndexOf(job, [0x1B, 0x69, 0x4B]) + 3]);
    }

    [Fact]
    public void Encode_WithCutMarks_PrintsOneUncutStrip_WithADashedMarkAtEveryCut()
    {
        using var label = WhiteLabel(10, 64);

        var job = BrotherPtRasterEncoder.Encode([label, label], NineMm, cutMode: CutMode.CutMarks);

        // One page (a single print command, no form feeds) with auto cut off.
        Assert.Equal(0, job.Count(b => b == 0x0C));
        Assert.Equal(0x00, job[IndexOf(job, [0x1B, 0x69, 0x4D]) + 3]);

        // 3 marks + 2 labels × (10 lines + 2 × 14 gap lines) = 79 raster lines.
        var info = IndexOf(job, [0x1B, 0x69, 0x7A]);
        Assert.Equal(3 + 2 * (10 + 2 * 14), BitConverter.ToInt32(job, info + 7));

        var raster = RasterSection(job);
        Assert.Equal([0x47, 17, 0x00, 15], raster[..4]); // starts with a mark
        var mark = raster[4..20];
        // 9 mm tape: printable pins start at bit 39. Pins 0-2 (bits 39-41) are set, 3-5 (42-44) clear.
        bool Bit(int bit) => (mark[bit / 8] & (0x80 >> (bit % 8))) != 0;
        Assert.True(Bit(39) && Bit(40) && Bit(41));
        Assert.False(Bit(42) || Bit(43) || Bit(44));
        Assert.False(Bit(38)); // outside the printable band
        Assert.Equal(3, CountSequence(raster, [0x47, 17, 0x00, 15]));
    }

    private static int CountSequence(byte[] data, byte[] pattern)
    {
        var count = 0;
        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern)) count++;
        }

        return count;
    }

    [Theory]
    [InlineData("9mm(0.35\")", 9.0)]
    [InlineData("3.5mm(0.14\")", 3.5)]
    [InlineData(" 24 mm", 24.0)]
    [InlineData("No Tape", null)]
    [InlineData(null, null)]
    public void LoadedTapeMm_ReadsTheWidthFromThePrintersMediaName(string? mediaName, double? expected)
    {
        var snapshot = new PrinterStatusSnapshot(3, [0], "READY", 0, mediaName);

        Assert.Equal(expected is null ? null : (decimal)expected, snapshot.LoadedTapeMm);
    }

    [Theory]
    [InlineData(3.5, 3.5)]
    [InlineData(9, 9)]
    [InlineData(10, 9)]
    [InlineData(29, 24)]
    public void ForLabelHeight_PicksTheClosestTapeWidth(double heightMm, double expectedTapeMm)
    {
        Assert.Equal((decimal)expectedTapeMm, BrotherPtTape.ForLabelHeight((decimal)heightMm).WidthMm);
    }
}
