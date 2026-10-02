using System.Globalization;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.PrintData;

/// <param name="Kind">"text" (CSV or text file), "spreadsheet", or "json" (a list of records, whose
/// property names are always the header row).</param>
/// <param name="Sheets">The workbook's sheet names; empty for a text file.</param>
/// <param name="Separator">"," ";" "|" …, "tab", "space", or "none" for one value per line;
/// null for a spreadsheet.</param>
/// <param name="SeparatorDetected">False when the file doesn't say clearly which separator it
/// uses: the user should be asked, and <paramref name="Separator"/> is only a first guess.</param>
/// <param name="HasHeader">The first row names the columns, and isn't a label itself.</param>
/// <param name="Rows">Every row, the header included. All rows have the same number of cells.</param>
/// <param name="Fields">Field name → column index, for the fields matched by the header.</param>
public sealed record PrintDataResponse(
    string Kind,
    IReadOnlyList<string> Sheets,
    int Sheet,
    string? Separator,
    bool SeparatorDetected,
    bool HasHeader,
    List<string[]> Rows,
    Dictionary<string, int> Fields,
    int? QuantityColumn);

/// <summary>Reads a data file (CSV, text, JSON or Excel) for bulk printing, and matches its columns to
/// a label's fields. The file is only read; nothing is stored.</summary>
public static class PrintDataReader
{
    public const long MaxSizeBytes = 100 * 1024 * 1024;

    /// <summary>Labels per bulk job (rows, without the header).</summary>
    public const int MaxRows = 5000;

    /// <param name="separator">The user's choice (as in <see cref="PrintDataResponse.Separator"/>);
    /// null detects it.</param>
    public static PrintDataResponse? Read(
        byte[] bytes,
        string? separator,
        int? sheet,
        CultureInfo culture,
        IReadOnlyCollection<TemplateField> fields,
        out string? error)
    {
        error = null;
        List<string[]> rows;
        IReadOnlyList<string> sheets = [];
        var readSheet = 0;
        string? usedSeparator = null;
        var detected = true;
        var spreadsheet = SpreadsheetReader.LooksLikeWorkbook(bytes);

        if (spreadsheet)
        {
            try
            {
                var contents = SpreadsheetReader.Read(bytes, sheet, culture, MaxRows + 1);
                (sheets, readSheet, rows) = (contents.Sheets, contents.Sheet, contents.Rows);
            }
            catch (Exception ex) when (ex is ExcelDataReader.Exceptions.ExcelReaderException or InvalidDataException or IOException or NotSupportedException)
            {
                error = "This Excel file can't be read. Save it again as .xlsx, or export it as CSV.";
                return null;
            }
        }
        else
        {
            if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 4096)) >= 0 && bytes is not ([0xFF, 0xFE, ..] or [0xFE, 0xFF, ..]))
            {
                error = "This isn't a text file. Use an Excel file (.xlsx), a CSV, text or JSON file.";
                return null;
            }

            var text = TextFileDecoder.Decode(bytes);
            char? character;

            if (JsonDataReader.LooksLikeJson(text))
            {
                var records = JsonDataReader.Read(text, MaxRows + 1, out error);

                if (error is not null)
                {
                    return null;
                }

                if (records is not null)
                {
                    return FromJson(records, fields, out error);
                }
            }

            if (separator is null)
            {
                (character, detected) = SeparatorDetector.Detect(text);
            }
            else if (!TryParseSeparator(separator, out character))
            {
                error = "The separator must be a single character.";
                return null;
            }

            rows = DelimitedTextReader.Read(text, character, MaxRows + 2);
            usedSeparator = SeparatorName(character);
        }

        if (rows.Count == 0)
        {
            error = "The file has no data.";
            return null;
        }

        // Short rows are filled up, so every row has a cell for every column.
        var width = rows.Max(row => row.Length);
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Length < width)
            {
                var filled = new string[width];
                Array.Fill(filled, string.Empty);
                rows[i].CopyTo(filled, 0);
                rows[i] = filled;
            }
        }

        var match = ColumnMatcher.Match(rows, fields);

        if (rows.Count - (match.HasHeader ? 1 : 0) > MaxRows)
        {
            error = $"The file has more than {MaxRows} rows. Split it into several files.";
            return null;
        }

        return new PrintDataResponse(
            spreadsheet ? "spreadsheet" : "text", sheets, readSheet, usedSeparator, detected,
            match.HasHeader, rows, match.Fields, match.QuantityColumn);
    }

    /// <summary>JSON records as a table: the property names are the header, whether or not one of
    /// them names a field.</summary>
    private static PrintDataResponse? FromJson(List<string[]> rows, IReadOnlyCollection<TemplateField> fields, out string? error)
    {
        error = null;

        if (rows.Count < 2 || rows[0].Length == 0)
        {
            error = "The file has no data.";
            return null;
        }

        if (rows.Count - 1 > MaxRows)
        {
            error = $"The file has more than {MaxRows} rows. Split it into several files.";
            return null;
        }

        var match = ColumnMatcher.Match(rows, fields);
        return new PrintDataResponse("json", [], 0, null, true, true, rows, match.Fields, match.QuantityColumn);
    }

    private static bool TryParseSeparator(string value, out char? separator)
    {
        separator = value switch
        {
            "none" => null,
            "tab" => '\t',
            "space" => ' ',
            { Length: 1 } => value[0],
            _ => '\0'
        };
        return separator != '\0' && separator is not ('"' or '\n' or '\r');
    }

    private static string SeparatorName(char? separator) => separator switch
    {
        null => "none",
        '\t' => "tab",
        ' ' => "space",
        _ => separator.Value.ToString()
    };
}
