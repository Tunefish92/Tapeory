using SkiaSharp;

namespace Tapeory.Api.Rendering;

/// <summary>Parses the color strings the browser editor produces: "#rgb", "#rrggbb", and the
/// literal "transparent". Anything else falls back to the given default rather than throwing,
/// since a bad color value shouldn't abort rendering the whole label.</summary>
public static class ColorParser
{
    public static SKColor Parse(string? value, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();

        if (trimmed.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return SKColors.Transparent;
        }

        if (trimmed.Length is 4 or 7 && trimmed[0] == '#')
        {
            var hex = trimmed[1..];

            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(c => new string(c, 2)));
            }

            if (byte.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return new SKColor(r, g, b);
            }
        }

        return fallback;
    }
}
