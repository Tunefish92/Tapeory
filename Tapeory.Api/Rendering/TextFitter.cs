namespace Tapeory.Api.Rendering;

/// <summary>How a text box copes with content that is too big for it.</summary>
public enum TextFitMode
{
    /// <summary>Draw at the chosen size; long text may run past the box.</summary>
    None,

    /// <summary>Keep the lines as typed and shrink the font until everything fits.</summary>
    Shrink,

    /// <summary>Wrap onto new lines at spaces (never inside a word), shrinking only if the
    /// wrapped text still doesn't fit.</summary>
    Wrap,
}

public sealed record FittedText(float FontSize, IReadOnlyList<string> Lines);

/// <summary>
/// Fits text into a fixed box. Pure layout math over a <c>measure(text, fontSize)</c> callback,
/// so it's testable without fonts and mirrors the editor's fitText.ts line for line — keep the
/// two in sync.
/// </summary>
public static class TextFitter
{
    /// <summary>Line spacing as a multiple of the font size (same as the editor).</summary>
    public const float LineHeightFactor = 1.2f;

    /// <summary>Smallest size shrinking may go to: about 1pt, in mm.</summary>
    public const float MinFontSize = 0.35f;

    private const float Epsilon = 0.001f;

    public static TextFitMode ParseMode(string? value) => value switch
    {
        "shrink" => TextFitMode.Shrink,
        "wrap" => TextFitMode.Wrap,
        _ => TextFitMode.None,
    };

    public static FittedText Fit(
        string text,
        float boxWidth,
        float boxHeight,
        float fontSize,
        TextFitMode mode,
        Func<string, float, float> measure)
    {
        var paragraphs = text.Replace("\r\n", "\n").Split('\n');

        if (mode == TextFitMode.None || boxWidth <= 0 || boxHeight <= 0 || fontSize <= 0)
        {
            return new FittedText(fontSize, paragraphs);
        }

        IReadOnlyList<string> Layout(float size) =>
            mode == TextFitMode.Wrap ? paragraphs.SelectMany(p => WrapParagraph(p, boxWidth, size, measure)).ToList() : paragraphs;

        bool Fits(IReadOnlyList<string> lines, float size) =>
            lines.Count * size * LineHeightFactor <= boxHeight + Epsilon
            && lines.All(line => measure(line, size) <= boxWidth + Epsilon);

        var atFullSize = Layout(fontSize);
        if (Fits(atFullSize, fontSize))
        {
            return new FittedText(fontSize, atFullSize);
        }

        // Smaller text never needs more lines (every width scales down together), so fitting is
        // monotonic in the font size and a binary search finds the largest size that fits.
        var low = Math.Min(MinFontSize, fontSize);
        var high = fontSize;
        var best = Layout(low);

        for (var i = 0; i < 20; i++)
        {
            var mid = (low + high) / 2f;
            var lines = Layout(mid);

            if (Fits(lines, mid))
            {
                low = mid;
                best = lines;
            }
            else
            {
                high = mid;
            }
        }

        return new FittedText(low, best);
    }

    /// <summary>Greedy word wrap. A word wider than the box gets a line of its own rather than
    /// being cut — the caller then shrinks until it fits.</summary>
    internal static IEnumerable<string> WrapParagraph(string paragraph, float width, float size, Func<string, float, float> measure)
    {
        var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var current = words[0];
        foreach (var word in words.Skip(1))
        {
            var candidate = current + " " + word;
            if (measure(candidate, size) <= width + Epsilon)
            {
                current = candidate;
            }
            else
            {
                yield return current;
                current = word;
            }
        }

        yield return current;
    }
}
