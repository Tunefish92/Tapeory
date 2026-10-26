using Tapeory.Api.Barcodes;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Rendering;

namespace Tapeory.Api.PrintData;

/// <summary>Rows of field values to check before a bulk print; at most
/// <see cref="RowChecker.MaxRowsPerRequest"/> per request.</summary>
public sealed record CheckRowsRequest(List<Dictionary<string, string>?>? Rows);

/// <param name="Errors">Why this row can't be printed.</param>
/// <param name="Warnings">What may look wrong on the label; the row can still be printed.</param>
public sealed record CheckedRow(List<string> Errors, List<string> Warnings);

public sealed record CheckRowsResponse(List<CheckedRow> Rows);

/// <summary>Checks rows the way a print job would treat them: required fields, barcodes, and a
/// trial render of each label.</summary>
public static class RowChecker
{
    public const int MaxRowsPerRequest = 100;

    // The trial render only has to succeed, so it's small.
    private const float TrialDpi = 96f;

    public static CheckedRow Check(
        IReadOnlyCollection<TemplateField> fields,
        RenderableDocument document,
        Dictionary<string, string> values,
        LabelRenderer renderer,
        ImageResolver resolveImage)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var validation = FieldValueValidator.Validate(fields, values);
        if (!validation.IsValid)
        {
            errors.AddRange(validation.Errors);
            return new CheckedRow(errors, warnings);
        }

        var resolved = FieldValueValidator.ResolveValues(fields, values);
        errors.AddRange(BarcodeValidation.Validate(document, resolved));

        if (errors.Count == 0)
        {
            try
            {
                using var _ = renderer.RenderBitmap(document, resolved, resolveImage, TrialDpi, TrialDpi);
                warnings.AddRange(renderer.TextWarnings(document, resolved));
            }
            catch (Exception)
            {
                errors.Add("This label can't be rendered.");
            }
        }

        return new CheckedRow(errors, warnings);
    }
}
