using System.Globalization;
using ExcelDataReader;
using ExcelNumberFormat;

namespace Tapeory.Api.PrintData;

public sealed record SpreadsheetContents(IReadOnlyList<string> Sheets, int Sheet, List<string[]> Rows);

/// <summary>Reads one sheet of an Excel workbook (.xlsx or .xls) as text, with numbers and dates
/// written the way Excel shows them.</summary>
public static class SpreadsheetReader
{
    // Excel's built-in "short date", which follows the reader's own region instead of a fixed pattern.
    private const int ShortDateFormat = 14;

    static SpreadsheetReader() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    public static bool LooksLikeWorkbook(byte[] bytes) =>
        bytes is [0x50, 0x4B, 0x03, 0x04, ..] or [0xD0, 0xCF, 0x11, 0xE0, ..];

    /// <param name="sheet">The sheet to read; null takes the first one with data.</param>
    /// <param name="maxRows">Reading stops one row past this, so the caller can tell there were more.</param>
    public static SpreadsheetContents Read(byte[] bytes, int? sheet, CultureInfo culture, int maxRows)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var names = new List<string>();
        var index = 0;
        int? read = null;
        var rows = new List<string[]>();

        do
        {
            names.Add(reader.Name);

            if (read is null && (sheet is null || sheet == index))
            {
                var found = ReadSheet(reader, culture, maxRows);

                if (found.Count > 0 || sheet == index)
                {
                    rows = found;
                    read = index;
                }
            }

            index++;
        }
        while (reader.NextResult());

        return new SpreadsheetContents(names, read ?? 0, rows);
    }

    private static List<string[]> ReadSheet(IExcelDataReader reader, CultureInfo culture, int maxRows)
    {
        var rows = new List<string[]>();

        while (rows.Count <= maxRows && reader.Read())
        {
            var cells = new string[reader.FieldCount];

            for (var column = 0; column < cells.Length; column++)
            {
                cells[column] = CellText(reader, column, culture).Trim();
            }

            if (cells.Any(cell => cell.Length > 0))
            {
                rows.Add(cells);
            }
        }

        // Drop columns that are empty in every row (Excel often reports a wider sheet than is used).
        var width = rows.Count == 0 ? 0 : rows.Max(row => Array.FindLastIndex(row, cell => cell.Length > 0) + 1);
        return rows.Select(row => row.Length == width ? row : row[..width]).ToList();
    }

    private static string CellText(IExcelDataReader reader, int column, CultureInfo culture)
    {
        var value = reader.GetValue(column);

        switch (value)
        {
            case null:
                return string.Empty;
            case string text:
                return text;
            case bool flag:
                return flag ? "TRUE" : "FALSE";
        }

        if (value is DateTime date && reader.GetNumberFormatIndex(column) == ShortDateFormat)
        {
            return date.ToString("d", culture);
        }

        var format = reader.GetNumberFormatString(column);

        if (!string.IsNullOrEmpty(format))
        {
            var numberFormat = new NumberFormat(format);

            if (numberFormat.IsValid)
            {
                return numberFormat.Format(value, culture);
            }
        }

        return Convert.ToString(value, culture) ?? string.Empty;
    }
}
