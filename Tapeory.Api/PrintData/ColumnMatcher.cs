using System.Globalization;
using System.Text;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.PrintData;

/// <param name="HasHeader">The first row names the columns.</param>
/// <param name="Fields">Field name → column index, for the fields that have a column.</param>
/// <param name="QuantityColumn">The column with the copies per row, if there is one.</param>
public sealed record ColumnMatch(bool HasHeader, Dictionary<string, int> Fields, int? QuantityColumn);

/// <summary>Decides whether a table's first row is a header, and which column fills which field
/// of the label.</summary>
public static class ColumnMatcher
{
    private static readonly string[] QuantityHeaders =
        ["quantity", "qty", "copies", "count", "anzahl", "menge", "kopien", "cantidad", "copias", "quantite", "copies", "quantita", "copie"];

    public static ColumnMatch Match(IReadOnlyList<string[]> rows, IReadOnlyCollection<TemplateField> fields)
    {
        var mapping = new Dictionary<string, int>();

        if (rows.Count == 0)
        {
            return new ColumnMatch(false, mapping, null);
        }

        var header = rows[0].Select(Normalize).ToArray();

        // A column goes to the field whose name it carries; labels come second, so a column
        // called like one field's name isn't taken by another field's label.
        foreach (var key in new Func<TemplateField, string?>[] { field => field.Name, field => field.Label })
        {
            foreach (var field in fields.Where(field => !mapping.ContainsKey(field.Name)))
            {
                var wanted = Normalize(key(field) ?? string.Empty);
                var column = wanted.Length == 0 ? -1 : Array.FindIndex(header, (cell) => cell == wanted);

                if (column >= 0 && !mapping.ContainsValue(column))
                {
                    mapping[field.Name] = column;
                }
            }
        }

        if (mapping.Count > 0)
        {
            var quantity = Array.FindIndex(header, cell => QuantityHeaders.Contains(cell));
            return new ColumnMatch(true, mapping, quantity >= 0 && !mapping.ContainsValue(quantity) ? quantity : null);
        }

        // No header. With one column and one field there is nothing to ask.
        if (fields.Count == 1 && rows.All(row => row.Length == 1))
        {
            mapping[fields.First().Name] = 0;
        }

        return new ColumnMatch(false, mapping, null);
    }

    /// <summary>For comparing names: no case, accents or extra spaces ("  Stück-Nr. " = "stuck-nr.").</summary>
    public static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsWhiteSpace(c) ? ' ' : char.ToLowerInvariant(c));
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
