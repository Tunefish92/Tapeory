using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Rendering;

public sealed record FieldValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static FieldValidationResult Valid() => new(true, []);

    public static FieldValidationResult Invalid(IReadOnlyList<string> errors) => new(false, errors);
}

/// <summary>Confirms a submitted set of field values satisfies a template version's required
/// fields, before rendering or queuing a print job with them.</summary>
public static class FieldValueValidator
{
    public static FieldValidationResult Validate(
        IEnumerable<TemplateField> fields,
        IReadOnlyDictionary<string, string> values)
    {
        var errors = new List<string>();

        foreach (var field in fields)
        {
            if (!field.Required)
            {
                continue;
            }

            var hasValue = values.TryGetValue(field.Name, out var value) && !string.IsNullOrWhiteSpace(value);
            var hasDefault = !string.IsNullOrWhiteSpace(field.DefaultValue);

            if (!hasValue && !hasDefault)
            {
                errors.Add($"A value is required for field '{field.Name}'.");
            }
        }

        return errors.Count == 0 ? FieldValidationResult.Valid() : FieldValidationResult.Invalid(errors);
    }

    /// <summary>Merges submitted values over each field's default, for fields not explicitly
    /// provided — the same fallback a preview or print render should use.</summary>
    public static Dictionary<string, string> ResolveValues(
        IEnumerable<TemplateField> fields,
        IReadOnlyDictionary<string, string> submittedValues)
    {
        var resolved = new Dictionary<string, string>();

        foreach (var field in fields)
        {
            resolved[field.Name] = submittedValues.TryGetValue(field.Name, out var value) && value.Length > 0
                ? value
                : field.DefaultValue ?? string.Empty;
        }

        return resolved;
    }
}
