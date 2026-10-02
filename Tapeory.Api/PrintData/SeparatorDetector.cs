namespace Tapeory.Api.PrintData;

/// <param name="Separator">Null for a file with one value per line.</param>
/// <param name="Detected">False when the file doesn't say clearly, and the user has to choose.</param>
public sealed record SeparatorGuess(char? Separator, bool Detected);

/// <summary>Finds the separator of a CSV or text file from its first lines.</summary>
public static class SeparatorDetector
{
    public static readonly char[] Candidates = [',', ';', '\t', '|'];

    private const int LinesToLookAt = 30;

    public static SeparatorGuess Detect(string text)
    {
        // A separator fits when it splits every line into the same number of columns, two or more.
        var fitting = new List<(char Separator, int Columns)>();
        var anyCandidateInText = false;

        foreach (var candidate in Candidates)
        {
            if (!text.Contains(candidate))
            {
                continue;
            }
            anyCandidateInText = true;

            var rows = DelimitedTextReader.Read(text, candidate, LinesToLookAt);
            if (rows.Count > 0 && rows[0].Length >= 2 && rows.All(row => row.Length == rows[0].Length))
            {
                fitting.Add((candidate, rows[0].Length));
            }
        }

        if (fitting.Count == 0)
        {
            // No candidate anywhere: one value per line. Otherwise the lines are uneven.
            return new SeparatorGuess(null, Detected: !anyCandidateInText);
        }

        var most = fitting.Max(fit => fit.Columns);
        var best = fitting.Where(fit => fit.Columns == most).ToList();
        return best.Count == 1 ? new SeparatorGuess(best[0].Separator, true) : new SeparatorGuess(best[0].Separator, false);
    }
}
