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
