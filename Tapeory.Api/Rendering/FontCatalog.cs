using SkiaSharp;

namespace Tapeory.Api.Rendering;

/// <summary>A font file as stored on the server, ready to hand to the browser.</summary>
public sealed record FontFile(byte[] Data, string ContentType);

/// <summary>
/// The font families the label renderer can actually draw with: everything installed on the
/// server that has real Latin letters. The editor offers exactly this list, and can download the
/// same files so its preview uses the very fonts the printed label will.
/// </summary>
public sealed class FontCatalog
{
    // Icon/symbol fonts map letters to pictograms, which makes no sense for label text.
    private static readonly HashSet<string> SymbolFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Wingdings", "Wingdings 2", "Wingdings 3", "Webdings", "Symbol", "Marlett", "MT Extra",
        "Segoe MDL2 Assets", "Segoe Fluent Icons", "HoloLens MDL2 Assets", "Bookshelf Symbol 7",
        "MS Reference Specialty", "MS Outlook", "Font Awesome", "Material Icons", "OpenSymbol",
    };

    private readonly Lazy<IReadOnlyList<string>> _families;

    public FontCatalog() : this(() => SKFontManager.Default.FontFamilies)
    {
    }

    /// <summary>For tests: a fixed set of candidate family names instead of the system's.</summary>
    public FontCatalog(Func<IEnumerable<string>> candidateFamilies)
    {
        _families = new Lazy<IReadOnlyList<string>>(() => Load(candidateFamilies()));
    }

    /// <summary>Installed, text-capable families, sorted by name.</summary>
    public IReadOnlyList<string> Families => _families.Value;

    /// <summary>The catalog's own spelling of <paramref name="family"/>, or null if it isn't installed.</summary>
    public string? Find(string? family) =>
        string.IsNullOrWhiteSpace(family)
            ? null
            : Families.FirstOrDefault(f => string.Equals(f, family.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The raw font file for a family (bold face if requested and available), or null
    /// when the family isn't in the catalog or its data can't be read.</summary>
    public FontFile? GetFontFile(string family, bool bold)
    {
        var canonical = Find(family);
        if (canonical is null)
        {
            return null;
        }

        using var typeface = SKTypeface.FromFamilyName(
            canonical,
            bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);

        // Skia silently substitutes a default face for unknown names — never ship that under
        // the requested name.
        if (typeface is null || !string.Equals(typeface.FamilyName, canonical, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var stream = typeface.OpenStream(out _);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        var data = buffer.ToArray();
        return data.Length == 0 ? null : new FontFile(data, DetectContentType(data));
    }

    public static string DetectContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return "application/octet-stream";
        }

        return data[..4] switch
        {
            [(byte)'O', (byte)'T', (byte)'T', (byte)'O'] => "font/otf",
            [(byte)'t', (byte)'t', (byte)'c', (byte)'f'] => "font/collection",
            [(byte)'w', (byte)'O', (byte)'F', (byte)'F'] => "font/woff",
            [(byte)'w', (byte)'O', (byte)'F', (byte)'2'] => "font/woff2",
            _ => "font/ttf",
        };
    }

    private static IReadOnlyList<string> Load(IEnumerable<string> candidates)
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var family in candidates)
        {
            if (string.IsNullOrWhiteSpace(family)
                || family.StartsWith('@') // Windows' vertical-writing aliases
                || family.StartsWith('.') // macOS private system faces
                || SymbolFonts.Contains(family)
                || result.Contains(family))
            {
                continue;
            }

            using var typeface = SKTypeface.FromFamilyName(family);
            if (typeface is null || !string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var font = new SKFont(typeface);
            if (font.ContainsGlyphs("AaZz09"))
            {
                result.Add(family);
            }
        }

        return [.. result];
    }
}
