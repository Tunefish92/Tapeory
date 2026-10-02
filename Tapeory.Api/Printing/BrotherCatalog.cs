namespace Tapeory.Api.Printing;

/// <summary>Print head families: they share a raster format and a media table.</summary>
public enum BrotherFamily
{
    /// <summary>P-touch with a 128-pin, 180 dpi head, TZe tape up to 24 mm.</summary>
    Pt180,

    /// <summary>P-touch with a 560-pin, 360 dpi head, TZe tape up to 36 mm (PT-P900 series).</summary>
    Pt360,

    /// <summary>QL label printers with a 720-pin, 300 dpi head, DK rolls up to 62 mm.</summary>
    Ql720,

    /// <summary>Wide QL label printers with a 1296-pin, 300 dpi head, DK rolls up to 104 mm.</summary>
    Ql1296
}

public enum MediaKind
{
    /// <summary>P-touch TZe/HGe tape.</summary>
    Tape,

    /// <summary>P-touch HSe heat-shrink tube.</summary>
    Tube,

    /// <summary>QL DK continuous-length roll.</summary>
    Continuous,

    /// <summary>QL DK die-cut labels, fixed length.</summary>
    DieCut,

    /// <summary>QL DK round die-cut labels.</summary>
    Round
}

/// <param name="WidthMm">Across the print head (tape width).</param>
/// <param name="LengthMm">Along the feed, for die-cut labels; 0 for tape and rolls.</param>
/// <param name="WidthCode">The width byte of the print information command.</param>
/// <param name="PrintPins">Head pins that can print on this media.</param>
/// <param name="OffsetPins">Unused pins before the print area, in raster-line byte order (the
/// first byte's most significant bit is pin 0).</param>
/// <param name="PrintLengthDots">Die-cut labels: printable length along the feed at the head's
/// native dpi; the raster line count must match it.</param>
public sealed record BrotherMedia(
    string Id,
    MediaKind Kind,
    decimal WidthMm,
    decimal LengthMm,
    byte WidthCode,
    int PrintPins,
    int OffsetPins,
    int PrintLengthDots = 0,
    bool TwoColor = false)
{
    public bool IsDieCut => Kind is MediaKind.DieCut or MediaKind.Round;

    public bool IsQl => Kind is MediaKind.Continuous or MediaKind.DieCut or MediaKind.Round;
}

/// <summary>
/// A Brother printer model: its head family and the commands and options it supports.
/// </summary>
/// <param name="Network">Has Wi-Fi or Ethernet (a raw port Tapeory can print to directly);
/// USB-only models print through a CUPS server's raw queue instead.</param>
/// <param name="AutoCut">Has an automatic cutter.</param>
/// <param name="HalfCut">Can half-cut (PT).</param>
/// <param name="HighResolution">Doubles the resolution along the feed (PT 360/720 dpi, QL 600 dpi).</param>
/// <param name="Compression">Accepts TIFF (PackBits) compressed raster lines.</param>
/// <param name="ModeSwitch">Needs "ESC i a 1" to enter raster mode (older QL models are always in it).</param>
/// <param name="ExpandedMode">Supports the "ESC i K" expanded/advanced mode command.</param>
/// <param name="TwoColor">Prints black and red on DK-22251 (QL-800 series).</param>
/// <param name="InvalidateBytes">NUL bytes that reset the printer before a job.</param>
/// <param name="CutEvery">Supports "ESC i A" (cut every n labels).</param>
public sealed record BrotherModel(
    string Name,
    BrotherFamily Family,
    bool Network,
    bool AutoCut = true,
    bool HalfCut = false,
    bool HighResolution = false,
    bool Compression = true,
    bool ModeSwitch = true,
    bool ExpandedMode = true,
    bool TwoColor = false,
    int InvalidateBytes = 100,
    bool CutEvery = true)
{
    public bool IsQl => Family is BrotherFamily.Ql720 or BrotherFamily.Ql1296;

    public int HeadPins => Family switch
    {
        BrotherFamily.Pt180 => 128,
        BrotherFamily.Pt360 => 560,
        BrotherFamily.Ql720 => 720,
        _ => 1296
    };

    /// <summary>Dots per inch across the head (and along the feed at standard quality).</summary>
    public int Dpi => Family switch
    {
        BrotherFamily.Pt180 => 180,
        BrotherFamily.Pt360 => 360,
        _ => 300
    };

    public IReadOnlyList<BrotherMedia> Media => BrotherCatalog.MediaFor(Family);
}

/// <summary>
/// Brother printers Tapeory prints on, and their media. Everything here comes from Brother's
/// official "Raster Command Reference" manuals: PT-E550W/P750W/P710BT, PT-H500/P700/E500,
/// PT-E310BT/E510/E560BT, PT-P900/P900W/P950NW (and P910BT), QL-800/810W/820NWB,
/// QL-1100/1110NWB, QL-600/710W/720NW, and the older QL series. The media tables list, per tape
/// or roll, the printable pins and the unused pins before them.
/// </summary>
public static class BrotherCatalog
{
    public static readonly IReadOnlyList<BrotherModel> Models =
    [
        // P-touch, 128 pins at 180 dpi.
        new("PT-P700", BrotherFamily.Pt180, Network: false),
        new("PT-H500", BrotherFamily.Pt180, Network: false),
        new("PT-E500", BrotherFamily.Pt180, Network: false),
        new("PT-P750W", BrotherFamily.Pt180, Network: true, HalfCut: true, HighResolution: true),
        new("PT-E550W", BrotherFamily.Pt180, Network: true, HalfCut: true, HighResolution: true),
        new("PT-P710BT", BrotherFamily.Pt180, Network: false, HighResolution: true),
        new("PT-E310BT", BrotherFamily.Pt180, Network: false, HalfCut: true, InvalidateBytes: 200),
        new("PT-E510", BrotherFamily.Pt180, Network: false, HalfCut: true, HighResolution: true, InvalidateBytes: 200),
        new("PT-E560BT", BrotherFamily.Pt180, Network: false, HalfCut: true, HighResolution: true, InvalidateBytes: 200),

        // P-touch, 560 pins at 360 dpi.
        new("PT-P900", BrotherFamily.Pt360, Network: false, HalfCut: true, HighResolution: true, InvalidateBytes: 200),
        new("PT-P900W", BrotherFamily.Pt360, Network: true, HalfCut: true, HighResolution: true, InvalidateBytes: 200),
        new("PT-P950NW", BrotherFamily.Pt360, Network: true, HalfCut: true, HighResolution: true, InvalidateBytes: 200),
        new("PT-P910BT", BrotherFamily.Pt360, Network: false, HalfCut: true, InvalidateBytes: 200),

        // QL, 720 pins at 300 dpi. The older models (QL Series Command Reference) differ: only
        // some switch to raster mode, cut every n labels, take expanded-mode settings or compress
        // (QL-650TD only over serial), and the QL-500 has no cutter.
        new("QL-500", BrotherFamily.Ql720, Network: false, AutoCut: false, Compression: false, ModeSwitch: false, ExpandedMode: false, InvalidateBytes: 200, CutEvery: false),
        new("QL-550", BrotherFamily.Ql720, Network: false, Compression: false, ModeSwitch: false, ExpandedMode: false, InvalidateBytes: 200, CutEvery: false),
        new("QL-560", BrotherFamily.Ql720, Network: false, Compression: false, ModeSwitch: false, ExpandedMode: false, InvalidateBytes: 200, CutEvery: false),
        new("QL-570", BrotherFamily.Ql720, Network: false, HighResolution: true, Compression: false, ModeSwitch: false, InvalidateBytes: 200),
        new("QL-580N", BrotherFamily.Ql720, Network: true, HighResolution: true, InvalidateBytes: 200),
        new("QL-650TD", BrotherFamily.Ql720, Network: false, HighResolution: true, Compression: false, InvalidateBytes: 200, CutEvery: false),
        new("QL-700", BrotherFamily.Ql720, Network: false, HighResolution: true, Compression: false, ModeSwitch: false, InvalidateBytes: 200),
        new("QL-600", BrotherFamily.Ql720, Network: false, HighResolution: true, InvalidateBytes: 200),
        new("QL-710W", BrotherFamily.Ql720, Network: true, HighResolution: true, InvalidateBytes: 200),
        new("QL-720NW", BrotherFamily.Ql720, Network: true, HighResolution: true, InvalidateBytes: 200),
        new("QL-800", BrotherFamily.Ql720, Network: false, HighResolution: true, Compression: false, TwoColor: true, InvalidateBytes: 400),
        new("QL-810W", BrotherFamily.Ql720, Network: true, HighResolution: true, TwoColor: true, InvalidateBytes: 400),
        new("QL-820NWB", BrotherFamily.Ql720, Network: true, HighResolution: true, TwoColor: true, InvalidateBytes: 400),

        // QL, 1296 pins at 300 dpi.
        new("QL-1050", BrotherFamily.Ql1296, Network: false, HighResolution: true, InvalidateBytes: 200),
        new("QL-1050N", BrotherFamily.Ql1296, Network: true, HighResolution: true, InvalidateBytes: 200),
        new("QL-1060N", BrotherFamily.Ql1296, Network: true, HighResolution: true, InvalidateBytes: 200),
        new("QL-1100", BrotherFamily.Ql1296, Network: false, HighResolution: true, InvalidateBytes: 350),
        new("QL-1110NWB", BrotherFamily.Ql1296, Network: true, HighResolution: true, InvalidateBytes: 350),
        new("QL-1115NWB", BrotherFamily.Ql1296, Network: true, HighResolution: true, InvalidateBytes: 350)
    ];

    // Widths in mm; pins and offsets from the Raster Command References' "raster line" tables.
    private static readonly IReadOnlyList<BrotherMedia> Pt180Media =
    [
        new("tze-3.5", MediaKind.Tape, 3.5m, 0, 4, 24, 52),
        new("tze-6", MediaKind.Tape, 6, 0, 6, 32, 48),
        new("tze-9", MediaKind.Tape, 9, 0, 9, 50, 39),
        new("tze-12", MediaKind.Tape, 12, 0, 12, 70, 29),
        new("tze-18", MediaKind.Tape, 18, 0, 18, 112, 8),
        new("tze-24", MediaKind.Tape, 24, 0, 24, 128, 0),
        new("hse-5.2", MediaKind.Tube, 5.2m, 0, 5, 20, 54),
        new("hse-5.8", MediaKind.Tube, 5.8m, 0, 6, 28, 50),
        new("hse-8.8", MediaKind.Tube, 8.8m, 0, 9, 48, 40),
        new("hse-9", MediaKind.Tube, 9, 0, 9, 44, 42),
        new("hse-11.2", MediaKind.Tube, 11.2m, 0, 11, 50, 39),
        new("hse-11.7", MediaKind.Tube, 11.7m, 0, 12, 66, 31),
        new("hse-17.7", MediaKind.Tube, 17.7m, 0, 18, 106, 11),
        new("hse-21", MediaKind.Tube, 21, 0, 21, 120, 4),
        new("hse-23.6", MediaKind.Tube, 23.6m, 0, 24, 128, 0)
    ];

    private static readonly IReadOnlyList<BrotherMedia> Pt360Media =
    [
        new("tze-3.5", MediaKind.Tape, 3.5m, 0, 4, 48, 248),
        new("tze-6", MediaKind.Tape, 6, 0, 6, 64, 240),
        new("tze-9", MediaKind.Tape, 9, 0, 9, 106, 219),
        new("tze-12", MediaKind.Tape, 12, 0, 12, 150, 197),
        new("tze-18", MediaKind.Tape, 18, 0, 18, 234, 155),
        new("tze-24", MediaKind.Tape, 24, 0, 24, 320, 112),
        new("tze-36", MediaKind.Tape, 36, 0, 36, 454, 45),
        new("hse-5.2", MediaKind.Tube, 5.2m, 0, 5, 40, 252),
        new("hse-5.8", MediaKind.Tube, 5.8m, 0, 6, 56, 244),
        new("hse-8.8", MediaKind.Tube, 8.8m, 0, 9, 96, 224),
        new("hse-9", MediaKind.Tube, 9, 0, 9, 88, 228),
        new("hse-11.2", MediaKind.Tube, 11.2m, 0, 11, 100, 222),
        new("hse-11.7", MediaKind.Tube, 11.7m, 0, 12, 132, 206),
        new("hse-17.7", MediaKind.Tube, 17.7m, 0, 18, 212, 166),
        new("hse-21", MediaKind.Tube, 21, 0, 21, 240, 152),
        new("hse-23.6", MediaKind.Tube, 23.6m, 0, 24, 256, 144),
        new("hse-31", MediaKind.Tube, 31, 0, 31, 360, 92)
    ];

    // QL: the offset is the right-margin column of Brother's tables, which comes first in the
    // raster line. Die-cut print lengths are the label length less 35 dots at each end.
    private static readonly IReadOnlyList<BrotherMedia> Ql720Media =
    [
        new("dk-12", MediaKind.Continuous, 12, 0, 12, 106, 29),
        new("dk-29", MediaKind.Continuous, 29, 0, 29, 306, 6),
        new("dk-38", MediaKind.Continuous, 38, 0, 38, 413, 12),
        new("dk-50", MediaKind.Continuous, 50, 0, 50, 554, 12),
        new("dk-54", MediaKind.Continuous, 54, 0, 54, 590, 0),
        new("dk-62", MediaKind.Continuous, 62, 0, 62, 696, 12),
        new("dk-62-red", MediaKind.Continuous, 62, 0, 62, 696, 12, TwoColor: true),
        new("dk-17x54", MediaKind.DieCut, 17, 54, 17, 165, 0, 566),
        new("dk-17x87", MediaKind.DieCut, 17, 87, 17, 165, 0, 956),
        new("dk-23x23", MediaKind.DieCut, 23, 23, 23, 236, 42, 202),
        new("dk-29x42", MediaKind.DieCut, 29, 42, 29, 306, 6, 425),
        new("dk-29x90", MediaKind.DieCut, 29, 90, 29, 306, 6, 991),
        new("dk-38x90", MediaKind.DieCut, 38, 90, 38, 413, 12, 991),
        new("dk-39x48", MediaKind.DieCut, 39, 48, 39, 425, 6, 495),
        new("dk-52x29", MediaKind.DieCut, 52, 29, 52, 578, 0, 271),
        new("dk-54x29", MediaKind.DieCut, 54, 29, 54, 602, 59, 271),
        new("dk-60x86", MediaKind.DieCut, 60, 86, 60, 672, 24, 954),
        new("dk-62x29", MediaKind.DieCut, 62, 29, 62, 696, 12, 271),
        new("dk-62x100", MediaKind.DieCut, 62, 100, 62, 696, 12, 1109),
        new("dk-d12", MediaKind.Round, 12, 12, 12, 94, 113, 94),
        new("dk-d24", MediaKind.Round, 24, 24, 24, 236, 42, 236),
        new("dk-d58", MediaKind.Round, 58, 58, 58, 618, 51, 618)
    ];

    private static readonly IReadOnlyList<BrotherMedia> Ql1296Media =
    [
        new("dk-12", MediaKind.Continuous, 12, 0, 12, 106, 74),
        new("dk-29", MediaKind.Continuous, 29, 0, 29, 306, 50),
        new("dk-38", MediaKind.Continuous, 38, 0, 38, 413, 56),
        new("dk-50", MediaKind.Continuous, 50, 0, 50, 554, 56),
        new("dk-54", MediaKind.Continuous, 54, 0, 54, 590, 44),
        new("dk-62", MediaKind.Continuous, 62, 0, 62, 696, 44),
        new("dk-102", MediaKind.Continuous, 102, 0, 102, 1164, 56),
        new("dk-103", MediaKind.Continuous, 103.6m, 0, 104, 1200, 38),
        new("dk-17x54", MediaKind.DieCut, 17, 54, 17, 165, 44, 566),
        new("dk-17x87", MediaKind.DieCut, 17, 87, 17, 165, 44, 956),
        new("dk-23x23", MediaKind.DieCut, 23, 23, 23, 236, 85, 202),
        new("dk-29x42", MediaKind.DieCut, 29, 42, 29, 306, 50, 425),
        new("dk-29x90", MediaKind.DieCut, 29, 90, 29, 306, 50, 991),
        new("dk-38x90", MediaKind.DieCut, 38, 90, 38, 413, 56, 991),
        new("dk-39x48", MediaKind.DieCut, 39, 48, 39, 425, 50, 495),
        new("dk-52x29", MediaKind.DieCut, 52, 29, 52, 578, 44, 271),
        new("dk-60x86", MediaKind.DieCut, 60, 86, 60, 672, 68, 954),
        new("dk-62x29", MediaKind.DieCut, 62, 29, 62, 696, 56, 271),
        new("dk-62x100", MediaKind.DieCut, 62, 100, 62, 696, 56, 1109),
        new("dk-102x51", MediaKind.DieCut, 102, 51, 102, 1164, 56, 526),
        new("dk-102x152", MediaKind.DieCut, 102, 152, 102, 1164, 56, 1660),
        new("dk-103x164", MediaKind.DieCut, 103, 164, 104, 1200, 38, 1822)
    ];

    public static IReadOnlyList<BrotherMedia> MediaFor(BrotherFamily family) => family switch
    {
        BrotherFamily.Pt180 => Pt180Media,
        BrotherFamily.Pt360 => Pt360Media,
        BrotherFamily.Ql720 => Ql720Media,
        _ => Ql1296Media
    };

    /// <summary>
    /// The model a printer's free-text model name refers to, ignoring case, spaces, dashes and a
    /// "Brother"/"c" suffix variant ("Brother PT-P750W", "pt p750w", "QL-820NWBc"). Unknown names
    /// fall back to the PT-P750W family, which is how Tapeory printed before it knew models.
    /// </summary>
    public static BrotherModel Find(string? name)
    {
        var normalized = Normalize(name);

        return Models
                   .Where(model => normalized.Contains(Normalize(model.Name)))
                   .MaxBy(model => model.Name.Length) // "QL-1110NWB" wins over "QL-1100"-like prefixes
               ?? Models.Single(model => model.Name == "PT-P750W");
    }

    /// <summary>Whether the name matches a known model, as opposed to falling back.</summary>
    public static bool IsKnown(string? name) =>
        Models.Any(model => Normalize(name).Contains(Normalize(model.Name)));

    private static string Normalize(string? value) =>
        new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>
    /// The media a label prints on: a die-cut label whose size matches the template (either way
    /// round), otherwise the tape or roll whose width is closest to the label's height. Templates
    /// made for an HSe heat-shrink tube get tube, and on two-colour printers a template made for
    /// DK-22251 gets the black/red roll.
    /// </summary>
    public static (BrotherMedia Media, bool Rotated) MediaForLabel(
        BrotherModel model, decimal widthMm, decimal heightMm, string? mediaId = null)
    {
        const decimal tolerance = 1.5m;

        // The die-cut label closest in size, either way round. (The first one within the tolerance
        // isn't enough: a round 24 mm label is also within 1.5 mm of the 23 × 23 mm square one.)
        var dieCut = model.Media
            .Where(media => media.IsDieCut)
            .SelectMany(media => new[]
            {
                (Media: media, Rotated: false, Across: Math.Abs(media.WidthMm - heightMm), Along: Math.Abs(media.LengthMm - widthMm)),
                (Media: media, Rotated: true, Across: Math.Abs(media.WidthMm - widthMm), Along: Math.Abs(media.LengthMm - heightMm))
            })
            .Where(match => match.Across <= tolerance && match.Along <= tolerance)
            .OrderBy(match => match.Across + match.Along)
            .ThenBy(match => match.Rotated)
            .Select(match => ((BrotherMedia Media, bool Rotated)?)(match.Media, match.Rotated))
            .FirstOrDefault();

        if (dieCut is not null)
        {
            return dieCut.Value;
        }

        // Heat-shrink tube only for a template made for one (an HSe preset); otherwise tape.
        var wantsRed = model.TwoColor && string.Equals(mediaId, "DK-22251", StringComparison.OrdinalIgnoreCase);
        var wantsTube = mediaId?.StartsWith("HSe", StringComparison.OrdinalIgnoreCase) == true;

        var candidates = model.Media
            .Where(media => !media.IsDieCut && media.TwoColor == wantsRed && (media.Kind == MediaKind.Tube) == wantsTube)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = [.. model.Media.Where(media => media.Kind is MediaKind.Tape or MediaKind.Continuous && !media.TwoColor)];
        }

        return (candidates.MinBy(media => Math.Abs(media.WidthMm - heightMm))!, false);
    }
}
