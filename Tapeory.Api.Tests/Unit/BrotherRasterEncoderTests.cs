using SkiaSharp;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;

namespace Tapeory.Api.Tests.Unit;

public sealed class BrotherRasterEncoderTests
{
    private static readonly BrotherModel P750W = BrotherCatalog.Find("PT-P750W");
    private static readonly BrotherMedia NineMm = Media(P750W, "tze-9");

    private static BrotherMedia Media(BrotherModel model, string id) => model.Media.Single(media => media.Id == id);

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

        var job = BrotherRasterEncoder.Encode([label], P750W, NineMm);

        Assert.All(job[..100], b => Assert.Equal(0, b));
        Assert.Equal([0x1B, 0x40, 0x1B, 0x69, 0x61, 0x01], job[100..106]);
    }

    [Fact]
    public void Encode_WritesTheTapeWidthAndRasterLineCount_InThePrintInformation()
    {
        using var label = WhiteLabel(300, 64);

        var job = BrotherRasterEncoder.Encode([label], P750W, NineMm);
        var info = IndexOf(job, [0x1B, 0x69, 0x7A]);

        Assert.Equal(9, job[info + 5]);
        Assert.Equal(300, BitConverter.ToInt32(job, info + 7));
    }

    [Fact]
    public void Encode_SendsBlankColumnsAsEmptyLines_AndEndsWithPrintAndFeed()
    {
        using var label = WhiteLabel(3, 64);

        var raster = RasterSection(BrotherRasterEncoder.Encode([label], P750W, NineMm));

        Assert.Equal([0x5A, 0x5A, 0x5A, 0x1A], raster);
    }

    [Fact]
    public void Encode_MapsTheTopPrintableRowToTheFirstPinAfterTheTapeMargin()
    {
        // 64 rows on 9 mm tape (50 printable pins): rows 7..56 print, so row 7 is the first pin,
        // which sits after the 39-pin margin.
        using var label = WhiteLabel(1, 64);
        label.SetPixel(0, 7, SKColors.Black);

        var raster = RasterSection(BrotherRasterEncoder.Encode([label], P750W, NineMm));

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

        var raster = RasterSection(BrotherRasterEncoder.Encode([label], P750W, NineMm));

        Assert.Equal([0x5A, 0x1A], raster);
    }

    [Fact]
    public void Encode_SeparatesCopiesWithFormFeed_AndMarksLaterPagesAsNotFirst()
    {
        using var label = WhiteLabel(2, 64);

        var job = BrotherRasterEncoder.Encode([label, label, label], P750W, NineMm);

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

        var job = BrotherRasterEncoder.Encode([label], P750W, NineMm, highResolution);

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

        var job = BrotherRasterEncoder.Encode([label, label, label], P750W, NineMm, cutMode: cutMode);

        Assert.Equal(0x40, job[IndexOf(job, [0x1B, 0x69, 0x4D]) + 3]); // auto cut on
        Assert.Equal(cutEvery, job[IndexOf(job, [0x1B, 0x69, 0x41]) + 3]);
        Assert.Equal(mode, job[IndexOf(job, [0x1B, 0x69, 0x4B]) + 3]);
    }

    [Fact]
    public void Encode_WithCutMarks_PrintsOneUncutStrip_WithADashedMarkAtEveryCut()
    {
        using var label = WhiteLabel(10, 64);

        var job = BrotherRasterEncoder.Encode([label, label], P750W, NineMm, cutMode: CutMode.CutMarks);

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
        Assert.Equal((decimal)expectedTapeMm, BrotherCatalog.MediaForLabel(P750W, 50, (decimal)heightMm).Media.WidthMm);
    }

    // --- QL and 360 dpi P-touch -------------------------------------------------------------

    private static bool Bit(byte[] line, int bit) => (line[bit / 8] & (0x80 >> (bit % 8))) != 0;

    /// <summary>The raster lines after the page setup, as (command byte, plane, data) tuples.</summary>
    private static List<(byte Command, byte Plane, byte[] Data)> QlLines(byte[] job, int bytesPerLine)
    {
        var lines = new List<(byte, byte, byte[])>();
        for (var i = 0; i < job.Length - 2; i++)
        {
            if ((job[i] == 0x67 && job[i + 1] == 0x00 && job[i + 2] == bytesPerLine)
                || (job[i] == 0x77 && job[i + 1] is 1 or 2 && job[i + 2] == bytesPerLine))
            {
                lines.Add((job[i], job[i + 1], job[(i + 3)..(i + 3 + bytesPerLine)]));
                i += 2 + bytesPerLine;
            }
        }

        return lines;
    }

    [Fact]
    public void Ql_OnAContinuousRoll_SendsTheFullSetup_And90ByteLines()
    {
        var model = BrotherCatalog.Find("QL-820NWB");
        using var label = WhiteLabel(3, 732); // 62 mm at 300 dpi
        label.SetPixel(1, 18, SKColors.Black); // first printable row (732 - 696) / 2

        var job = BrotherRasterEncoder.Encode([label], model, Media(model, "dk-62"));

        Assert.All(job[..400], b => Assert.Equal(0, b)); // 400-byte invalidate on the QL-800 series
        Assert.Equal([0x1B, 0x40, 0x1B, 0x69, 0x61, 0x01], job[400..406]);
        var info = IndexOf(job, [0x1B, 0x69, 0x7A]);
        Assert.Equal([0xCE, 0x0A, 62, 0], job[(info + 3)..(info + 7)]); // continuous 62 mm
        Assert.Equal(3, BitConverter.ToInt32(job, info + 7));
        Assert.Equal(0x40, job[IndexOf(job, [0x1B, 0x69, 0x4D]) + 3]); // auto cut
        Assert.Equal(0x08, job[IndexOf(job, [0x1B, 0x69, 0x4B]) + 3]); // cut at end
        Assert.Equal(35, job[IndexOf(job, [0x1B, 0x69, 0x64]) + 3]);   // 3 mm feed

        var lines = QlLines(job, 90);
        Assert.Equal(3, lines.Count);
        Assert.True(Bit(lines[1].Data, 12));   // 62 mm: the print area starts 12 pins in
        Assert.Equal(1, lines[1].Data.Sum(b => System.Numerics.BitOperations.PopCount(b)));
        Assert.Equal(0x1A, job[^1]);
    }

    [Fact]
    public void Ql_LeavesOutTheCommandsOlderModelsDontKnow()
    {
        using var label = WhiteLabel(2, 732);
        var ql700 = BrotherCatalog.Find("QL-700");
        var ql500 = BrotherCatalog.Find("QL-500");

        var job700 = BrotherRasterEncoder.Encode([label], ql700, Media(ql700, "dk-62"));
        var job500 = BrotherRasterEncoder.Encode([label], ql500, Media(ql500, "dk-62"));

        Assert.Equal(-1, IndexOf(job700, [0x1B, 0x69, 0x61])); // no raster-mode switch
        Assert.Equal(-1, IndexOf(job700, [0x4D, 0x00]));       // no compression command
        Assert.Equal(-1, IndexOf(job500, [0x1B, 0x69, 0x4D])); // QL-500: no cutter
        Assert.Equal(-1, IndexOf(job500, [0x1B, 0x69, 0x4B])); // ...and no expanded mode
    }

    [Fact]
    public void Ql_WideModels_Send162ByteLines_WithTheirOwnOffsets()
    {
        var model = BrotherCatalog.Find("QL-1110NWB");
        using var label = WhiteLabel(2, 732);
        label.SetPixel(0, 18, SKColors.Black);

        var job = BrotherRasterEncoder.Encode([label], model, Media(model, "dk-62"));

        Assert.All(job[..350], b => Assert.Equal(0, b));
        var lines = QlLines(job, 162);
        Assert.Equal(2, lines.Count);
        Assert.True(Bit(lines[0].Data, 44)); // QL-1100 series, 62 mm: 44 pins before the print area
    }

    [Fact]
    public void Ql_DieCutLabels_AreTrimmedToTheirPrintableLength_WithoutFeed()
    {
        var model = BrotherCatalog.Find("QL-820NWB");
        var media = Media(model, "dk-62x29");
        using var label = WhiteLabel(343, 732); // 29 mm × 62 mm at 300 dpi

        var job = BrotherRasterEncoder.Encode([label], model, media);

        var info = IndexOf(job, [0x1B, 0x69, 0x7A]);
        Assert.Equal([0x0B, 62, 29], job[(info + 4)..(info + 7)]); // die-cut, 62 × 29 mm
        Assert.Equal(271, BitConverter.ToInt32(job, info + 7));     // printable length, not 343
        Assert.Equal(0, job[IndexOf(job, [0x1B, 0x69, 0x64]) + 3]);  // no feed between die-cut labels
        Assert.Equal(271, QlLines(job, 90).Count);
    }

    [Fact]
    public void Ql_OnBlackAndRedTape_SendsRedPixelsInTheirOwnPlane()
    {
        var model = BrotherCatalog.Find("QL-820NWB");
        var red = Media(model, "dk-62-red");
        using var label = WhiteLabel(1, 732);
        label.SetPixel(0, 18, SKColors.Black);
        label.SetPixel(0, 19, SKColors.Red);

        var job = BrotherRasterEncoder.Encode([label], model, red);

        Assert.Equal(0x09, job[IndexOf(job, [0x1B, 0x69, 0x4B]) + 3]); // cut at end + two-colour
        var lines = QlLines(job, 90);
        Assert.Equal([(byte)1, (byte)2], lines.Select(l => l.Plane));
        Assert.True(Bit(lines[0].Data, 12) && !Bit(lines[0].Data, 13));  // black plane
        Assert.True(Bit(lines[1].Data, 13) && !Bit(lines[1].Data, 12));  // red plane
    }

    [Fact]
    public void Pt360_Sends70ByteLines_WithTheP900Offsets()
    {
        var model = BrotherCatalog.Find("Brother PT-P950NW");
        var media = Media(model, "tze-36");
        using var label = WhiteLabel(1, 511); // 36 mm at 360 dpi
        label.SetPixel(0, (511 - 454) / 2, SKColors.Black);

        var job = BrotherRasterEncoder.Encode([label], model, media, highResolution: true);

        Assert.All(job[..200], b => Assert.Equal(0, b));
        Assert.Equal(36, job[IndexOf(job, [0x1B, 0x69, 0x7A]) + 5]);
        Assert.Equal(0x48, job[IndexOf(job, [0x1B, 0x69, 0x4B]) + 3]); // cut at end + 720 dpi
        Assert.Equal(56, job[IndexOf(job, [0x1B, 0x69, 0x64]) + 3]);   // 2 mm at 720 dpi

        var raster = RasterSection(job);
        Assert.Equal([0x47, 71, 0x00, 69], raster[..4]); // G + one 70-byte literal run
        Assert.True(Bit(raster[4..74], 45));            // 36 mm: the print area starts at pin 45
    }

    [Theory]
    [InlineData("QL-820NWB", 29, 62, "dk-62x29", false)] // 29 mm long, 62 mm across the roll
    [InlineData("QL-820NWB", 62, 29, "dk-62x29", true)]  // designed the other way round
    [InlineData("QL-820NWB", 80, 62, "dk-62", false)]    // any length on the 62 mm roll
    [InlineData("QL-1110NWB", 100, 102, "dk-102", false)]
    [InlineData("PT-P900W", 50, 36, "tze-36", false)]
    public void MediaForLabel_PicksDieCutLabelsBySize_AndRollsByWidth(
        string modelName, double widthMm, double heightMm, string mediaId, bool rotated)
    {
        var model = BrotherCatalog.Find(modelName);

        var (media, turn) = BrotherCatalog.MediaForLabel(model, (decimal)widthMm, (decimal)heightMm);

        Assert.Equal(mediaId, media.Id);
        Assert.Equal(rotated, turn);
    }

    [Fact]
    public void MediaForLabel_KeepsATemplateMadeForAContinuousRollOnTheRoll()
    {
        var ql820 = BrotherCatalog.Find("QL-820NWB");

        // A new template: 62 × 29 mm on DK-22210, which is also the size of the die-cut DK-11209.
        Assert.Equal(("dk-29", false), Id(BrotherCatalog.MediaForLabel(ql820, 62, 29, "DK-22210")));
        Assert.Equal(("dk-62-red", false), Id(BrotherCatalog.MediaForLabel(ql820, 100, 62, "dk-22251")));

        // Without a roll, or made for the die-cut label, the size decides as before.
        Assert.Equal(("dk-62x29", true), Id(BrotherCatalog.MediaForLabel(ql820, 62, 29)));
        Assert.Equal(("dk-62x29", true), Id(BrotherCatalog.MediaForLabel(ql820, 62, 29, "DK-11209")));

        static (string, bool) Id((BrotherMedia Media, bool Rotated) match) => (match.Media.Id, match.Rotated);
    }

    [Fact]
    public void MediaForLabel_UsesTubeOnlyForHeatShrinkTemplates_AndRedTapeForDk22251()
    {
        var p750w = BrotherCatalog.Find("PT-P750W");
        var ql820 = BrotherCatalog.Find("QL-820NWB");

        Assert.Equal("tze-12", BrotherCatalog.MediaForLabel(p750w, 40, 11.7m).Media.Id);
        Assert.Equal("hse-11.7", BrotherCatalog.MediaForLabel(p750w, 40, 11.7m, "HSe-231").Media.Id);
        Assert.Equal("dk-62-red", BrotherCatalog.MediaForLabel(ql820, 80, 62, "DK-22251").Media.Id);
        Assert.Equal("dk-62", BrotherCatalog.MediaForLabel(BrotherCatalog.Find("QL-720NW"), 80, 62, "DK-22251").Media.Id);
    }

    [Theory]
    [InlineData("Brother PT-P750W", "PT-P750W")]
    [InlineData("pt p950nw", "PT-P950NW")]
    [InlineData("QL-820NWBc", "QL-820NWB")]
    [InlineData("QL-1110NWB", "QL-1110NWB")]
    [InlineData("PT-P900W", "PT-P900W")]
    [InlineData("something else", "PT-P750W")] // unknown: the model Tapeory always printed on
    public void Find_MatchesModelNamesLoosely(string name, string expected)
    {
        Assert.Equal(expected, BrotherCatalog.Find(name).Name);
    }

    [Fact]
    public void Capabilities_FollowTheModel()
    {
        Assert.Equal([(300, 300), (600, 300)], PrinterCapabilities.Resolutions("QL-820NWB").Select(r => (r.HorizontalDpi, r.VerticalDpi)));
        Assert.Equal([(360, 360), (720, 360)], PrinterCapabilities.Resolutions("PT-P900W").Select(r => (r.HorizontalDpi, r.VerticalDpi)));
        Assert.Equal([(360, 360)], PrinterCapabilities.Resolutions("PT-P910BT").Select(r => (r.HorizontalDpi, r.VerticalDpi)));

        Assert.Equal([CutMode.AutoCut, CutMode.CutAtEnd, CutMode.CutMarks], PrinterCapabilities.CutModes("QL-820NWB"));
        Assert.Equal([CutMode.CutMarks], PrinterCapabilities.CutModes("QL-500"));
        Assert.Contains(CutMode.HalfCut, PrinterCapabilities.CutModes("PT-P750W"));
        Assert.DoesNotContain(CutMode.HalfCut, PrinterCapabilities.CutModes("PT-P700"));
    }
}
