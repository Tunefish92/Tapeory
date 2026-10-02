using System.Text;

namespace Tapeory.Api.PrintData;

/// <summary>Reads CSV-like text into rows: values may be quoted, a quoted value may hold the
/// separator, line breaks and doubled quotes. Without a separator, every line is one value.</summary>
public static class DelimitedTextReader
{
    /// <param name="maxRows">Stop after this many rows (for looking at the start of a file).</param>
    public static List<string[]> Read(string text, char? separator, int maxRows = int.MaxValue)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        // A quote only opens a quoted value at the start of a value.
        var atStart = true;

        void EndValue()
        {
            row.Add(value.ToString());
            value.Clear();
            atStart = true;
        }

        void EndRow()
        {
            EndValue();
            // Skip empty lines.
            if (row.Any(cell => cell.Trim().Length > 0))
            {
                rows.Add(row.Select(cell => cell.Trim()).ToArray());
            }
            row.Clear();
        }

        for (var i = 0; i < text.Length && rows.Count < maxRows; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c != '"')
                {
                    value.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"' && atStart && separator is not null)
            {
                quoted = true;
                atStart = false;
            }
            else if (c == separator)
            {
                EndValue();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                EndRow();
            }
            else
            {
                value.Append(c);
                atStart = atStart && c == ' ';
            }
        }

        if (rows.Count < maxRows && (value.Length > 0 || row.Count > 0))
        {
            EndRow();
        }

        return rows;
    }
}
