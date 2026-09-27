namespace Tapeory.Api.Printing;

/// <summary>
/// A TZe tape width as the 128-pin, 180 dpi PT print heads (PT-P750W, PT-E550W, PT-P710BT and
/// relatives) see it: how many pins can print on it and how many unused pins sit on each side.
/// Values are from Brother's "PT-E550W/P750W/P710BT Raster Command Reference".
/// </summary>
public sealed record BrotherPtTape(decimal WidthMm, byte WidthCode, int PrintPins, int MarginPins)
{
    public const int HeadPins = 128;

    public static readonly IReadOnlyList<BrotherPtTape> All =
    [
        new(3.5m, 4, 24, 52),
        new(6m, 6, 32, 48),
        new(9m, 9, 50, 39),
        new(12m, 12, 70, 29),
        new(18m, 18, 112, 8),
        new(24m, 24, 128, 0)
    ];

    /// <summary>The tape whose width is closest to a label's height. The network port can't
    /// report which tape is loaded, so the label size decides; the printer itself stops with a
    /// tape mismatch error if it's wrong.</summary>
    public static BrotherPtTape ForLabelHeight(decimal heightMm) =>
        All.MinBy(tape => Math.Abs(tape.WidthMm - heightMm))!;
}
