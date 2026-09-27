namespace Tapeory.Api.Data.Entities;

public enum FileStorageCategory
{
    Image = 0,
    OriginalLbx = 1,
    PrintJobOutput = 2
}

public sealed class UploadedFile
{
    public int Id { get; set; }

    /// <summary>The generated, filesystem-safe name the file is actually stored under.</summary>
    public required string FileName { get; set; }

    /// <summary>The name the uploader's file had, kept only for display/download purposes.</summary>
    public required string OriginalFileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public FileStorageCategory Category { get; set; }

    /// <summary>Path relative to the storage root, e.g. "images/&lt;guid&gt;.png".</summary>
    public required string RelativePath { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The account that uploaded it. Others only get an image that's part of a template
    /// they can see; null for files from before accounts existed, or restored from a backup.</summary>
    public int? OwnerUserId { get; set; }
}
