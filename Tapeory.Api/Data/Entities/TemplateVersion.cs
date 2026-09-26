namespace Tapeory.Api.Data.Entities;

public sealed class TemplateVersion
{
    public int Id { get; set; }

    public int TemplateId { get; set; }

    public Template? Template { get; set; }

    public int VersionNumber { get; set; }

    public decimal WidthMm { get; set; }

    public decimal HeightMm { get; set; }

    /// <summary>Opaque editor payload produced by the browser canvas (Phase 3). The API stores and
    /// versions it without understanding its internal shape.</summary>
    public required string EditorJson { get; set; }

    public int? PreviewImageFileId { get; set; }

    public UploadedFile? PreviewImageFile { get; set; }

    public List<TemplateField> Fields { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
