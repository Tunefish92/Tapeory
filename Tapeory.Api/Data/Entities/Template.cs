namespace Tapeory.Api.Data.Entities;

public sealed class Template
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Category { get; set; }

    /// <summary>Comma-separated tags. Kept as a flat column since tag search/filtering isn't built yet.</summary>
    public string? TagsCsv { get; set; }

    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;

    public int? CurrentVersionId { get; set; }

    public TemplateVersion? CurrentVersion { get; set; }

    public List<TemplateVersion> Versions { get; set; } = [];

    /// <summary>The original file this template was imported from (e.g. a .lbx), preserved
    /// unchanged so it stays available for compatibility printing even where conversion was
    /// only partial. Null for templates created natively in the browser editor.</summary>
    public int? SourceLbxFileId { get; set; }

    public UploadedFile? SourceLbxFile { get; set; }

    public List<TemplateConversionWarning> ConversionWarnings { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when the user deletes the template. The row and its versions are kept only so
    /// print history and statistics (which need each job's label size) stay intact, and so jobs
    /// already queued still print; everywhere else the template no longer exists.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
