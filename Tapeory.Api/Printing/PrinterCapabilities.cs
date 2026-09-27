using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printing;

/// <param name="HorizontalDpi">Along the tape (the feed direction).</param>
/// <param name="VerticalDpi">Across the tape (the print head).</param>
public sealed record PrintResolution(PrintQuality Quality, int HorizontalDpi, int VerticalDpi);

/// <summary>What a printer model can do, from <see cref="BrotherCatalog"/>. Models are matched on
/// the printer's free-text model name, ignoring case, spaces and dashes ("PT-P750W", "p750w").</summary>
public static class PrinterCapabilities
{
    /// <summary>The standard resolution of the PT-P750W, the model Tapeory falls back to.</summary>
    public static readonly PrintResolution Standard = new(PrintQuality.Standard, 180, 180);

    /// <summary>The model's native resolution, and double along the feed where it supports high
    /// resolution (PT 360 or 720 dpi, QL 600 dpi).</summary>
    public static IReadOnlyList<PrintResolution> Resolutions(string? model)
    {
        var brother = BrotherCatalog.Find(model);
        var standard = new PrintResolution(PrintQuality.Standard, brother.Dpi, brother.Dpi);

        return brother.HighResolution
            ? [standard, new PrintResolution(PrintQuality.High, brother.Dpi * 2, brother.Dpi)]
            : [standard];
    }

    /// <summary>The requested resolution if the model supports it, otherwise standard.</summary>
    public static PrintResolution Resolve(string? model, PrintQuality quality)
    {
        var resolutions = Resolutions(model);
        return resolutions.FirstOrDefault(resolution => resolution.Quality == quality) ?? resolutions[0];
    }

    /// <summary>
    /// The cutting options the model offers. P-touch printers cut, half-cut where they can, and
    /// chain-print; QL printers cut each label or at the end (they have no chain printing or half
    /// cut); a model without a cutter only prints uncut strips with cut marks.
    /// </summary>
    public static IReadOnlyList<CutMode> CutModes(string? model)
    {
        var brother = BrotherCatalog.Find(model);

        if (!brother.AutoCut)
        {
            return [CutMode.CutMarks];
        }

        if (brother.IsQl)
        {
            return brother.CutEvery || brother.ExpandedMode
                ? [CutMode.AutoCut, CutMode.CutAtEnd, CutMode.CutMarks]
                : [CutMode.AutoCut, CutMode.CutMarks];
        }

        return brother.HalfCut
            ? [CutMode.AutoCut, CutMode.HalfCut, CutMode.CutAtEnd, CutMode.ChainPrinting, CutMode.CutMarks]
            : [CutMode.AutoCut, CutMode.CutAtEnd, CutMode.ChainPrinting, CutMode.CutMarks];
    }
}
