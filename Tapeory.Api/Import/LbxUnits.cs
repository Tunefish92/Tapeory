using System.Globalization;

namespace Tapeory.Api.Import;

/// <summary>
/// Brother's .lbx format expresses every measurement as a string like "175.7pt" — a decimal
/// number in typographic points (1/72 inch), suffixed with the literal unit. Observed
/// consistently across every attribute (paper size, object position/size, font size) in a real
/// P-touch Editor export.
/// </summary>
public static class LbxUnits
{
    private const decimal PointsToMillimeters = 0.352778m;

    /// <summary>Parses a value like "175.7pt" into its numeric point value, or null if the
    /// string is missing, blank, or not in the expected "&lt;number&gt;pt" shape.</summary>
    public static decimal? ParsePoints(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var numericPart = trimmed.EndsWith("pt", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^2]
            : trimmed;

        return decimal.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var points)
            ? points
            : null;
    }

    public static decimal PointsToMm(decimal points) => Math.Round(points * PointsToMillimeters, 2);

    /// <summary>Parses a "&lt;number&gt;pt" value straight to millimeters, or null if unparseable.</summary>
    public static decimal? ParsePointsAsMm(string? value)
    {
        var points = ParsePoints(value);
        return points is null ? null : PointsToMm(points.Value);
    }
}
