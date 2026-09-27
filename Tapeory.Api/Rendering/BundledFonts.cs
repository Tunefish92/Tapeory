using SkiaSharp;

namespace Tapeory.Api.Rendering;

/// <summary>
/// The font files that ship with Tapeory (the Fonts folder next to the app: Google Fonts such as
/// Roboto, Open Sans, Lato and Montserrat, each in Regular and Bold). They're available on every
/// machine whatever the operating system has installed, and win over an installed family of the
/// same name, so a label renders the same everywhere.
/// </summary>
public static class BundledFonts
{
    private sealed record Face(SKData Data, string ContentType);

    private sealed record Family(Face? Regular, Face? Bold);

    private static readonly Lazy<IReadOnlyDictionary<string, Family>> Families = new(Load);

    public static IEnumerable<string> FamilyNames => Families.Value.Keys;

    public static bool Contains(string family) => Families.Value.ContainsKey(family);

    /// <summary>A new typeface for the family (the bold face if asked for and shipped), or null
    /// if Tapeory doesn't bundle the family. The caller owns it.</summary>
    public static SKTypeface? Typeface(string family, bool bold) =>
        FaceFor(family, bold) is { } face ? SKTypeface.FromData(face.Data) : null;

    /// <summary>The raw file for the family, as the browser editor downloads it.</summary>
    public static FontFile? File(string family, bool bold) =>
        FaceFor(family, bold) is { } face ? new FontFile(face.Data.ToArray(), face.ContentType) : null;

    private static Face? FaceFor(string family, bool bold) =>
        Families.Value.TryGetValue(family, out var faces) ? (bold ? faces.Bold ?? faces.Regular : faces.Regular ?? faces.Bold) : null;

    private static IReadOnlyDictionary<string, Family> Load()
    {
        var families = new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase);
        var directory = Path.Combine(AppContext.BaseDirectory, "Fonts");

        if (!Directory.Exists(directory))
        {
            return families;
        }

        var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

        foreach (var path in files)
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            var data = SKData.CreateCopy(bytes);
            using var typeface = SKTypeface.FromData(data);

            if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName) || typeface.IsItalic)
            {
                continue;
            }

            var face = new Face(data, FontCatalog.DetectContentType(bytes));
            var existing = families.GetValueOrDefault(typeface.FamilyName) ?? new Family(null, null);
            families[typeface.FamilyName] = typeface.FontWeight >= (int)SKFontStyleWeight.SemiBold
                ? existing with { Bold = existing.Bold ?? face }
                : existing with { Regular = existing.Regular ?? face };
        }

        return families;
    }
}

/// <summary>Finds a font by family name: Tapeory's bundled fonts first, then the system's.</summary>
public static class FontResolver
{
    /// <summary>A typeface the caller owns. Like SKTypeface.FromFamilyName, it falls back to a
    /// default face when the family isn't found anywhere.</summary>
    public static SKTypeface Typeface(string family, bool bold) =>
        BundledFonts.Typeface(family, bold)
        ?? SKTypeface.FromFamilyName(
            family,
            bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
}
