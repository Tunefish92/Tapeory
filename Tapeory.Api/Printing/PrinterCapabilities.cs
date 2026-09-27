using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printing;

/// <param name="HorizontalDpi">Along the tape (the feed direction).</param>
/// <param name="VerticalDpi">Across the tape (the print head).</param>
public sealed record PrintResolution(PrintQuality Quality, int HorizontalDpi, int VerticalDpi);

/// <summary>What a printer model can do, as Brother documents it. Models are matched on the
/// printer's free-text model name, ignoring case, spaces and dashes ("PT-P750W", "p750w").</summary>
public static class PrinterCapabilities
{
    public static readonly PrintResolution Standard = new(PrintQuality.Standard, 180, 180);
    public static readonly PrintResolution High = new(PrintQuality.High, 360, 180);

    /// <summary>Models whose raster command reference lists the 180 × 360 dpi high-resolution
    /// mode.</summary>
    private static readonly string[] HighResolutionModels = ["P700", "P750W", "E550W", "P710BT"];

    public static IReadOnlyList<PrintResolution> Resolutions(string? model)
    {
        var normalized = new string((model ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        return HighResolutionModels.Any(normalized.Contains) ? [Standard, High] : [Standard];
    }

    /// <summary>The requested resolution if the model supports it, otherwise standard.</summary>
    public static PrintResolution Resolve(string? model, PrintQuality quality) =>
        Resolutions(model).FirstOrDefault(resolution => resolution.Quality == quality) ?? Standard;
}
