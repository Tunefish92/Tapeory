namespace Tapeory.Api.Templates;

/// <summary>A template's group is its Category column: free text, so a group exists as long as at
/// least one template uses that name.</summary>
public static class TemplateGroups
{
    // Must match the Template.Category column's HasMaxLength(100) in AppDbContext.
    public const int MaxLength = 100;

    /// <summary>Trims, and treats blank as "no group", so " Cables " and "Cables" are one group
    /// and an emptied field ungroups the template instead of storing an empty string.</summary>
    public static string? Normalize(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    public static string? Validate(string? group) =>
        Normalize(group) is { Length: > MaxLength }
            ? $"Group must be {MaxLength} characters or fewer."
            : null;
}

public sealed record RenameGroupRequest(string From, string? To);

public sealed record RenameGroupResponse(int UpdatedCount);
