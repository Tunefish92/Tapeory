using System.Xml.Linq;

namespace Tapeory.Api.Uploads;

/// <summary>
/// SVG is XML, so an uploaded ".svg" can carry a &lt;script&gt; element or "on*" event handler
/// attributes — a stored-XSS vector, since GetImage later serves it back as
/// image/svg+xml, and a browser navigated straight to that URL (not embedded in an &lt;img&gt;)
/// executes any script the file contains. Strips anything capable of running script before the
/// file is written to disk; rejects the upload outright if the content isn't parseable XML,
/// rather than storing something a lenient browser parser might still execute.
/// </summary>
public static class SvgSanitizer
{
    private static readonly HashSet<string> DisallowedElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "foreignObject", "iframe", "embed", "object", "use"
    };

    public static SvgSanitizeResult Sanitize(byte[] content)
    {
        XDocument document;

        try
        {
            using var stream = new MemoryStream(content);
            document = XDocument.Load(stream, LoadOptions.None);
        }
        catch (Exception)
        {
            return SvgSanitizeResult.Invalid("The file is not a valid SVG (XML could not be parsed).");
        }

        if (document.Root is null)
        {
            return SvgSanitizeResult.Invalid("The file is not a valid SVG (no root element).");
        }

        StripDangerousContent(document.Root);

        using var output = new MemoryStream();
        document.Save(output, SaveOptions.DisableFormatting);
        return SvgSanitizeResult.Valid(output.ToArray());
    }

    private static void StripDangerousContent(XElement element)
    {
        // Materialize before mutating — Elements()/Attributes() are lazy and Remove() would
        // otherwise invalidate the enumeration mid-iteration.
        foreach (var child in element.Elements().ToList())
        {
            if (DisallowedElementNames.Contains(child.Name.LocalName))
            {
                child.Remove();
                continue;
            }

            StripDangerousContent(child);
        }

        foreach (var attribute in element.Attributes().ToList())
        {
            var name = attribute.Name.LocalName;
            var value = attribute.Value.TrimStart();

            var isEventHandler = name.StartsWith("on", StringComparison.OrdinalIgnoreCase);
            var isScriptUri = value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);

            if (isEventHandler || isScriptUri)
            {
                attribute.Remove();
            }
        }
    }
}

public sealed record SvgSanitizeResult(bool IsValid, byte[]? SanitizedContent, string? Error)
{
    public static SvgSanitizeResult Valid(byte[] sanitizedContent) => new(true, sanitizedContent, null);

    public static SvgSanitizeResult Invalid(string error) => new(false, null, error);
}
