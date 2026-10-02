namespace Tapeory.Api.Printing;

/// <summary>The part of a label that can't be printed on, as a margin on each side in mm, as the
/// label lies in the editor (its height across the tape or roll).</summary>
/// <param name="FeedMarginMm">Blank tape the printer adds before and after the label (tape and
/// continuous rolls); it is outside the label, so nothing on the label is lost to it.</param>
/// <param name="MediaId">The tape or roll the limits are for.</param>
public sealed record PrintAreaResponse(
    decimal TopMm, decimal RightMm, decimal BottomMm, decimal LeftMm, decimal FeedMarginMm, string MediaId);

/// <summary>
/// Where a label can't be printed. This belongs to the tape or roll, not to a printer: Brother's
/// print heads are narrower than the media. Per the Raster Command References ("Print area"
/// tables), a 9 mm TZe tape is 64 dots wide at 180 dpi but only 50 dots (7.1 mm) print, and a
/// 36 mm tape (PT-P900 series, 360 dpi) is 512 dots wide with 454 (32 mm) printing, which leaves
/// about 2 mm at the top and the bottom. Die-cut DK labels also lose 35 dots (3 mm) at each end.
/// Where printer series differ for the same tape (24 mm: 18.1 mm print on a PT-P750W, 22.6 mm
/// on a PT-P900), the smaller area counts, so a design inside the line prints on any of them.
/// The numbers come from the media tables and the centring the raster encoder uses.
/// </summary>
public static class PrintArea
{
    private const decimal MmPerInch = 25.4m;

    /// <summary>One printer of each head family, to look its media up with.</summary>
    private static readonly BrotherModel[] Families =
    [
        BrotherCatalog.Find("PT-P750W"), BrotherCatalog.Find("PT-P900W"), BrotherCatalog.Find("QL-820NWB"), BrotherCatalog.Find("QL-1110NWB")
    ];

    /// <summary>The feed margin the encoder asks for: 2 mm on P-touch (14 dots at 180 dpi), 3 mm
    /// (35 dots) on QL continuous rolls.</summary>
    private static decimal FeedMarginMm(BrotherModel model, BrotherMedia media) =>
        media.IsDieCut ? 0 : model.IsQl ? 35 * MmPerInch / model.Dpi : 14 * MmPerInch / 180;

    /// <param name="mediaId">The medium chosen in the editor ("tze-12", "HSe-221", "DK-22210"),
    /// which says whether the label is for a P-touch tape or a QL roll; null looks at both.</param>
    public static PrintAreaResponse For(decimal widthMm, decimal heightMm, string? mediaId)
    {
        var wantsQl = mediaId?.StartsWith("dk", StringComparison.OrdinalIgnoreCase);
        var candidates = Families
            .Where(model => wantsQl is null || model.IsQl == wantsQl)
            .Select(model => For(model, widthMm, heightMm, mediaId))
            .ToList();

        // The media made for this size (a custom size takes the nearest one). Not every printer
        // series has every label: a round 24 mm one exists only for the narrow QL head.
        var nearest = candidates.Min(candidate => candidate.Distance);
        var fitting = candidates.Where(candidate => candidate.Distance <= nearest + 0.05m).ToList();

        // Where printer series differ, the largest margins: what every one of them leaves blank.
        return fitting.MaxBy(candidate => candidate.Area.TopMm + candidate.Area.BottomMm + candidate.Area.LeftMm + candidate.Area.RightMm)!.Area;
    }

    private static (PrintAreaResponse Area, decimal Distance) For(BrotherModel model, decimal widthMm, decimal heightMm, string? mediaId)
    {
        var (media, rotated) = BrotherCatalog.MediaForLabel(model, widthMm, heightMm, mediaId);
        var (acrossMm, alongMm) = rotated ? (widthMm, heightMm) : (heightMm, widthMm);
        var mmPerDot = MmPerInch / model.Dpi;

        // Across the head: the label is rendered as wide as it is, and the middle PrintPins print.
        var acrossDots = Dots(acrossMm, model.Dpi);
        var unused = Math.Max(0, acrossDots - media.PrintPins);
        var before = unused / 2 * mmPerDot;
        var after = (unused - unused / 2) * mmPerDot;

        // Along the feed: only die-cut labels have a fixed printable length, kept centred.
        var end = media.IsDieCut ? Math.Max(0, Dots(alongMm, model.Dpi) - media.PrintLengthDots) / 2m * mmPerDot : 0;

        var (top, bottom, left, right) = rotated ? (end, end, before, after) : (before, after, end, end);
        var area = new PrintAreaResponse(Round(top), Round(right), Round(bottom), Round(left), Round(FeedMarginMm(model, media)), media.Id);

        // How far the media's size is from the label's; die-cut labels have a length as well.
        return (area, Math.Abs(media.WidthMm - acrossMm) + (media.IsDieCut ? Math.Abs(media.LengthMm - alongMm) : 0));
    }

    // As LabelRenderer sizes its bitmap.
    private static int Dots(decimal mm, int dpi) => Math.Max(1, (int)Math.Ceiling(mm * dpi / MmPerInch));

    private static decimal Round(decimal mm) => Math.Round(mm, 2);
}
