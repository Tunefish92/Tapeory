using Tapeory.Api.Printing;

namespace Tapeory.Api.Tests.Unit;

/// <summary>The unprintable margins of each tape and roll, against Brother's Raster Command
/// Reference tables ("Print area": tape width in dots, print area in dots, width offset).</summary>
public sealed class PrintAreaTests
{
    private static decimal Mm(int dots, int dpi) => Math.Round(dots * 25.4m / dpi, 2);

    [Theory]
    // PT-P750W reference (180 dpi), width offset per tape: 3.5 mm: 0 dots, 6 mm: 5, 9 and 12 mm: 7,
    // 18 mm: 8, 24 mm: 21. Tapeory renders a label at its nominal height (12 mm is 86 dots,
    // Brother's tape is 84) and the middle of it prints, so a dot more can fall away. The
    // PT-P900 series prints more of these tapes; the smaller area counts.
    [InlineData(3.5, 0, 1, "tze-3.5")]
    [InlineData(6, 5, 6, "tze-6")]
    [InlineData(9, 7, 7, "tze-9")]
    [InlineData(12, 8, 8, "tze-12")]
    [InlineData(18, 8, 8, "tze-18")]
    [InlineData(24, 21, 22, "tze-24")]
    public void TzeTape_LosesWhatThePrintHeadCannotReach_AtTheTopAndTheBottom(decimal tapeMm, int topDots, int bottomDots, string mediaId)
    {
        var area = PrintArea.For(60, tapeMm, $"tze-{tapeMm}");

        Assert.Equal(mediaId, area.MediaId);
        Assert.Equal((Mm(topDots, 180), Mm(bottomDots, 180)), (area.TopMm, area.BottomMm));
        Assert.Equal((0m, 0m), (area.LeftMm, area.RightMm));
        // 2 mm of blank tape before and after the label, outside it.
        Assert.Equal(1.98m, area.FeedMarginMm);
    }

    [Fact]
    public void A36MmTape_HasItsOwnLimits_FromThePrintersThatTakeIt()
    {
        // PT-P900 reference (360 dpi): 512 dots wide, 454 print, 29 dots (2.03 mm) offset. 36 mm
        // is 511 dots here: 28 and 29.
        var area = PrintArea.For(100, 36, "tze-36");

        Assert.Equal("tze-36", area.MediaId);
        Assert.Equal((Mm(28, 360), Mm(29, 360)), (area.TopMm, area.BottomMm));
        Assert.Equal((1.98m, 2.05m), (area.TopMm, area.BottomMm));
    }

    [Fact]
    public void TheMediumSaysWhetherALabelIsForTapeOrForARoll()
    {
        var tape = PrintArea.For(60, 12, "tze-12");
        var roll = PrintArea.For(60, 12, "DK-22214");
        var unknown = PrintArea.For(60, 12, null);

        Assert.Equal("tze-12", tape.MediaId);
        // DK 12 mm: 142 dots at 300 dpi, 106 print.
        Assert.Equal(("dk-12", Mm(18, 300), Mm(18, 300)), (roll.MediaId, roll.TopMm, roll.BottomMm));
        // Without a medium, the larger margins of the two.
        Assert.Equal(roll.TopMm, unknown.TopMm);
    }

    [Fact]
    public void ADieCutLabel_AlsoLosesThreeMillimetresAtEachEnd()
    {
        var area = PrintArea.For(100, 62, "DK-11202");

        Assert.Equal("dk-62x100", area.MediaId);
        // 62 mm is 733 dots at 300 dpi, 696 print: 18 and 19 dots. 100 mm is 1182 dots, 1109 print.
        Assert.Equal((1.52m, 1.61m), (area.TopMm, area.BottomMm));
        Assert.Equal((3.09m, 3.09m), (area.LeftMm, area.RightMm));
        Assert.Equal(0m, area.FeedMarginMm);
    }

    [Fact]
    public void ADieCutLabelDesignedTheOtherWayRound_HasItsMarginsTurnedToo()
    {
        var area = PrintArea.For(62, 100, "DK-11202");

        Assert.Equal((3.09m, 3.09m), (area.TopMm, area.BottomMm));
        Assert.Equal((1.52m, 1.61m), (area.LeftMm, area.RightMm));
    }

    [Fact]
    public void ACustomHeight_TakesTheNearestTape()
    {
        // 3 mm is 22 dots; the 3.5 mm tape prints 24, so nothing is lost.
        var narrow = PrintArea.For(40, 3, null);
        // 30 mm is nearest to the 29 mm roll: 355 dots, 306 print.
        var between = PrintArea.For(40, 30, null);

        Assert.Equal((0m, 0m), (narrow.TopMm, narrow.BottomMm));
        Assert.Equal("dk-29", between.MediaId);
    }

    [Fact]
    public void RoundLabels_AreNotTakenForTheSquareOnesOfNearlyTheSameSize()
    {
        // Brother: 24 mm Dia is 284 dots, 236 print, 24 dots (2 mm) all round; 23 × 23 mm loses
        // 18 dots (1.5 mm) across and 35 (3 mm) at each end.
        var round = PrintArea.For(24, 24, "DK-11218");
        var square = PrintArea.For(23, 23, "DK-11221");

        Assert.Equal(("dk-d24", 2.03m, 2.03m, 2.03m, 2.03m), (round.MediaId, round.TopMm, round.BottomMm, round.LeftMm, round.RightMm));
        Assert.Equal(("dk-23x23", 1.52m, 2.96m), (square.MediaId, square.TopMm, square.LeftMm));
        Assert.Equal("dk-d24", BrotherCatalog.MediaForLabel(BrotherCatalog.Find("QL-820NWB"), 24, 24).Media.Id);
        Assert.Equal("dk-d12", BrotherCatalog.MediaForLabel(BrotherCatalog.Find("QL-820NWB"), 12, 12).Media.Id);
    }

    [Theory]
    // Every roll and die-cut label the editor offers, against the "Print area" tables of Brother's
    // QL-1100 reference: width offset 18 dots (1.5 mm), 23 for 54 mm, 12 for 103 mm; die-cut
    // labels 35 dots (3 mm) at each end, 72 for 102 × 152 mm, 59 for 103 × 164 mm; round labels
    // 24 dots (2 mm), 35 (3 mm) for 58 mm. Within 0.4 mm: Tapeory takes a label at its nominal size
    // (58 mm, where Brother's is 58.3 mm).
    [InlineData("DK-22214", 60, 12, 1.5, 0)]
    [InlineData("DK-22210", 60, 29, 1.5, 0)]
    [InlineData("DK-22225", 60, 38, 1.5, 0)]
    [InlineData("DK-22223", 60, 50, 1.5, 0)]
    [InlineData("DK-N55224", 60, 54, 1.9, 0)]
    [InlineData("DK-22205", 60, 62, 1.5, 0)]
    [InlineData("DK-22251", 60, 62, 1.5, 0)]
    [InlineData("DK-22243", 60, 102, 1.5, 0)]
    [InlineData("DK-22246", 60, 103.6, 1.0, 0)]
    [InlineData("DK-11219", 12, 12, 2.0, 2.0)]
    [InlineData("DK-11204", 54, 17, 1.5, 3.0)]
    [InlineData("DK-11203", 87, 17, 1.5, 3.0)]
    [InlineData("DK-11221", 23, 23, 1.5, 3.0)]
    [InlineData("DK-11218", 24, 24, 2.0, 2.0)]
    [InlineData("DK-11209", 29, 62, 1.5, 3.0)]
    [InlineData("DK-11201", 90, 29, 1.5, 3.0)]
    [InlineData("DK-11208", 90, 38, 1.5, 3.0)]
    [InlineData("dk-29x42", 42, 29, 1.5, 3.0)]
    [InlineData("dk-39x48", 48, 39, 1.5, 3.0)]
    [InlineData("dk-52x29", 29, 52, 1.5, 3.0)]
    [InlineData("dk-54x29", 29, 54, 1.5, 3.0)]
    [InlineData("DK-11207", 58, 58, 3.0, 3.0)]
    [InlineData("DK-11234", 86, 60, 1.5, 3.0)]
    [InlineData("DK-11202", 100, 62, 1.5, 3.0)]
    [InlineData("DK-11240", 50, 102, 1.5, 3.05)]
    [InlineData("DK-11241", 152, 102, 1.5, 6.1)]
    [InlineData("DK-11247", 164, 103, 1.0, 5.0)]
    public void EveryRollAndDieCutLabelOfTheEditor_MatchesBrothersTable(string medium, decimal widthMm, decimal heightMm, decimal across, decimal ends)
    {
        var area = PrintArea.For(widthMm, heightMm, medium);

        Assert.InRange(area.TopMm, across - 0.4m, across + 0.4m);
        Assert.InRange(area.BottomMm, across - 0.4m, across + 0.4m);
        Assert.InRange(area.LeftMm, Math.Max(0, ends - 0.4m), ends + 0.4m);
        Assert.InRange(area.RightMm, Math.Max(0, ends - 0.4m), ends + 0.4m);
    }

    [Theory]
    // The heat-shrink tubes of the editor, against the print areas of Brother's PT references
    // (print area in mm of the tube's flat width).
    [InlineData("HSe-211E", 5.2, 2.82)]
    [InlineData("HSe-221E", 9.0, 6.21)]
    [InlineData("HSe-231E", 11.2, 7.06)]
    [InlineData("HSe-241E", 17.7, 14.9)]
    [InlineData("HSe-251E", 21.0, 16.93)]
    [InlineData("HSe-261E", 31.0, 25.4)]
    [InlineData("HSe-211", 5.8, 3.9)]
    [InlineData("HSe-221", 8.8, 6.8)]
    [InlineData("HSe-231", 11.7, 9.3)]
    [InlineData("HSe-241", 17.7, 14.9)]
    [InlineData("HSe-251", 23.6, 18.1)]
    public void EveryHeatShrinkTubeOfTheEditor_HasBrothersPrintArea(string medium, decimal heightMm, decimal printableMm)
    {
        var area = PrintArea.For(60, heightMm, medium);

        Assert.InRange(heightMm - area.TopMm - area.BottomMm, printableMm - 0.2m, printableMm + 0.2m);
    }
}
