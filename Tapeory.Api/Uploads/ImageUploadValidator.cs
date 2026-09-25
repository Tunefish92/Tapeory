namespace Tapeory.Api.Uploads;

public static class ImageUploadValidator
{
    public const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/svg+xml"
    };

    public static ImageUploadValidationResult Validate(string? contentType, long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return ImageUploadValidationResult.Invalid("The uploaded file is empty.");
        }

        if (sizeBytes > MaxSizeBytes)
        {
            return ImageUploadValidationResult.Invalid(
                $"The uploaded file exceeds the {MaxSizeBytes / (1024 * 1024)} MB limit.");
        }

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
        {
            return ImageUploadValidationResult.Invalid(
                $"Unsupported content type '{contentType}'. Allowed types: {string.Join(", ", AllowedContentTypes)}.");
        }

        return ImageUploadValidationResult.Valid();
    }
}

public sealed record ImageUploadValidationResult(bool IsValid, string? Error)
{
    public static ImageUploadValidationResult Valid() => new(true, null);

    public static ImageUploadValidationResult Invalid(string error) => new(false, error);
}
