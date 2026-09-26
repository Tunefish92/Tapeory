namespace Tapeory.Api.Storage;

/// <summary>
/// Pure helpers for turning an untrusted, user-supplied file name into a safe one to store on
/// disk. The original name is never used for the stored file itself, only its extension.
/// </summary>
public static class SafeFileNaming
{
    private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();

    /// <summary>Extracts a lowercase, filesystem-safe extension (including the leading dot) from an
    /// untrusted file name, or "" if none can be safely determined.</summary>
    public static string GetSafeExtension(string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return string.Empty;
        }

        // Path.GetExtension operates on the last segment; strip any directory-like prefixes an
        // attacker might smuggle in (e.g. "../../evil.png") before extracting it.
        var fileNameOnly = originalFileName.Split('/', '\\')[^1];
        var extension = Path.GetExtension(fileNameOnly);

        if (string.IsNullOrEmpty(extension) || extension.Length > 10)
        {
            return string.Empty;
        }

        if (extension.IndexOfAny(InvalidPathChars) >= 0)
        {
            return string.Empty;
        }

        return extension.ToLowerInvariant();
    }

    /// <summary>Generates a new, collision-resistant file name that carries no information from the
    /// original upload other than its extension.</summary>
    public static string GenerateStoredFileName(string originalFileName)
    {
        return $"{Guid.NewGuid():N}{GetSafeExtension(originalFileName)}";
    }
}
