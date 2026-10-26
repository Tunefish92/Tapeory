using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.Templates;

public enum TemplateEditCheck
{
    Allowed,
    NotFound,
    /// <summary>Visible (it's public), but only its owner or an administrator may change it.</summary>
    Forbidden
}

public enum SetPreviewImageResult
{
    Success,
    TemplateNotFound,
    FileNotFound
}

public sealed class TemplateService(
    AppDbContext db,
    FileStorageService fileStorage,
    ILogger<TemplateService> logger,
    IHttpContextAccessor? http = null)
{
    /// <summary>The signed-in account's view. Background work (the print queue, backups) runs
    /// outside a request and sees everything.</summary>
    public TemplateAccess Access => TemplateAccess.For(http?.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal());

    /// <summary>Every lookup goes through this, so deleted templates, and private templates of
    /// other accounts, behave as if they were gone.</summary>
    private IQueryable<Template> ActiveTemplates() => Access.Visible(db.Templates.Where(template => template.DeletedAt == null));

    private IQueryable<Template> TemplatesWithCurrentVersion() =>
        ActiveTemplates()
            .Include(template => template.CurrentVersion!)
                .ThenInclude(version => version.Fields)
            .Include(template => template.ConversionWarnings)
            .Include(template => template.Owner);

    /// <summary>Whether the signed-in account may change template <paramref name="id"/>.</summary>
    public async Task<TemplateEditCheck> CheckEditAsync(int id, CancellationToken cancellationToken)
    {
        var template = await ActiveTemplates().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

        return template is null ? TemplateEditCheck.NotFound
            : Access.CanEdit(template) ? TemplateEditCheck.Allowed
            : TemplateEditCheck.Forbidden;
    }

    /// <summary>Makes a template public or private. A private template needs an owner, so an
    /// ownerless (shared) one made private belongs to whoever does it.</summary>
    public async Task<Template?> SetVisibilityAsync(int id, bool isPublic, CancellationToken cancellationToken)
    {
        var template = await TemplatesWithCurrentVersion().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
        {
            return null;
        }

        template.IsPublic = isPublic;

        if (!isPublic && template.OwnerUserId is null)
        {
            template.OwnerUserId = Access.UserId;
        }

        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(template).Reference(t => t.Owner).LoadAsync(cancellationToken);
        return template;
    }

    public async Task<Template> CreateAsync(CreateTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = new Template
        {
            Name = request.Name,
            Description = request.Description,
            Category = TemplateGroups.Normalize(request.Category),
            TagsCsv = TemplateMapper.SerializeTags(request.Tags)
        };
        Access.ClaimNew(template);

        var version = new TemplateVersion
        {
            VersionNumber = 1,
            WidthMm = request.WidthMm,
            HeightMm = request.HeightMm,
            EditorJson = request.EditorJson,
            Fields = ToFieldEntities(request.Fields)
        };

        template.Versions.Add(version);

        db.Templates.Add(template);
        // Template.CurrentVersionId and TemplateVersion.TemplateId form a cycle between two
        // brand-new rows with database-generated keys, which EF Core can't resolve in a single
        // SaveChanges — it needs version.Id from the first insert before it can point the
        // template at it.
        await db.SaveChangesAsync(cancellationToken);

        template.CurrentVersion = version;
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(template).Reference(t => t.Owner).LoadAsync(cancellationToken);

        return template;
    }

    public Task<Template?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        TemplatesWithCurrentVersion().SingleOrDefaultAsync(template => template.Id == id, cancellationToken);

    public async Task<List<Template>> ListAsync(
        string? search,
        string? category,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = TemplatesWithCurrentVersion();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(template => EF.Functions.Like(template.Name, $"%{search}%"));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(template => template.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TemplateStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(template => template.Status == parsedStatus);
        }

        return await query.OrderByDescending(template => template.UpdatedAt).ToListAsync(cancellationToken);
    }

    public async Task<Template?> UpdateMetadataAsync(
        int id,
        UpdateTemplateMetadataRequest request,
        CancellationToken cancellationToken)
    {
        var template = await TemplatesWithCurrentVersion()
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
        {
            return null;
        }

        template.Name = request.Name;
        template.Description = request.Description;
        template.Category = TemplateGroups.Normalize(request.Category);
        template.TagsCsv = TemplateMapper.SerializeTags(request.Tags);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<TemplateStatus>(request.Status, true, out var parsedStatus))
            {
                throw new ArgumentException($"Unknown template status '{request.Status}'.");
            }

            template.Status = parsedStatus;
        }

        template.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return template;
    }

    /// <summary>Moves every template in group <paramref name="from"/> to <paramref name="to"/>
    /// (blank = ungroup them). Matching follows the column's collation — case- and
    /// accent-insensitive under MySQL's default — the same way the UI groups them. Deliberately
    /// leaves UpdatedAt alone: reorganising isn't editing, and bumping it would reshuffle the list.</summary>
    public async Task<int> RenameGroupAsync(string from, string? to, CancellationToken cancellationToken)
    {
        var source = TemplateGroups.Normalize(from);

        if (source is null)
        {
            return 0;
        }

        var target = TemplateGroups.Normalize(to);

        return await Access.Editable(ActiveTemplates())
            .Where(template => template.Category == source)
            .ExecuteUpdateAsync(setters => setters.SetProperty(template => template.Category, target), cancellationToken);
    }

    public async Task<TemplateVersion?> AddVersionAsync(
        int templateId,
        CreateTemplateVersionRequest request,
        CancellationToken cancellationToken)
    {
        var template = await ActiveTemplates()
            .Include(t => t.Versions)
            .SingleOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template is null)
        {
            return null;
        }

        var nextVersionNumber = template.Versions.Count == 0
            ? 1
            : template.Versions.Max(v => v.VersionNumber) + 1;

        var version = new TemplateVersion
        {
            TemplateId = templateId,
            VersionNumber = nextVersionNumber,
            WidthMm = request.WidthMm,
            HeightMm = request.HeightMm,
            EditorJson = request.EditorJson,
            Fields = ToFieldEntities(request.Fields)
        };

        db.TemplateVersions.Add(version);
        template.CurrentVersion = version;
        template.Status = TemplateStatus.Draft;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return version;
    }

    /// <summary>Null both when the template doesn't exist and when it has no source .lbx — both
    /// cases are a 404 to the caller, so there's no need to distinguish them here.</summary>
    public async Task<UploadedFile?> GetSourceLbxFileAsync(int templateId, CancellationToken cancellationToken) =>
        await ActiveTemplates()
            .Where(template => template.Id == templateId)
            .Select(template => template.SourceLbxFile)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
        await ActiveTemplates().AnyAsync(template => template.Id == id, cancellationToken);

    /// <summary>Soft-deletes the template (see <see cref="Template.DeletedAt"/>) and removes its
    /// imported .lbx original, which nothing else needs. Returns false if it doesn't exist.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var template = await ActiveTemplates()
            .Include(t => t.SourceLbxFile)
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
        {
            return false;
        }

        var sourceLbx = template.SourceLbxFile;
        template.DeletedAt = DateTimeOffset.UtcNow;

        if (sourceLbx is not null)
        {
            template.SourceLbxFile = null;
            db.UploadedFiles.Remove(sourceLbx);
        }

        await db.SaveChangesAsync(cancellationToken);

        // After SaveChanges, so a failure here only leaves an orphaned file behind.
        if (sourceLbx is not null)
        {
            try
            {
                fileStorage.Delete(sourceLbx.RelativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not delete the original .lbx {Path} of template {Id}.", sourceLbx.RelativePath, id);
            }
        }

        return true;
    }

    public async Task<List<TemplateVersion>> ListVersionsAsync(int templateId, CancellationToken cancellationToken) =>
        await db.TemplateVersions
            .Include(version => version.Fields)
            .Where(version => version.TemplateId == templateId)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(cancellationToken);

    public async Task<SetPreviewImageResult> SetPreviewImageAsync(
        int templateId,
        int uploadedFileId,
        CancellationToken cancellationToken)
    {
        var template = await ActiveTemplates()
            .Include(t => t.CurrentVersion)
            .SingleOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template?.CurrentVersion is null)
        {
            return SetPreviewImageResult.TemplateNotFound;
        }

        var fileExists = await db.UploadedFiles.AnyAsync(f => f.Id == uploadedFileId, cancellationToken);

        if (!fileExists)
        {
            return SetPreviewImageResult.FileNotFound;
        }

        template.CurrentVersion.PreviewImageFileId = uploadedFileId;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return SetPreviewImageResult.Success;
    }

    /// <summary>
    /// Copies a template's current design, fields, size, group, tags, description and preview
    /// image into a new draft with its own version history. Uploaded images are shared, which is
    /// safe because they're never deleted along with a template; the original .lbx is not, since
    /// it belongs to (and is deleted with) the source template.
    /// </summary>
    public async Task<Template?> DuplicateAsync(int id, string name, CancellationToken cancellationToken)
    {
        var source = await GetByIdAsync(id, cancellationToken);

        if (source?.CurrentVersion is not { } version)
        {
            return null;
        }

        var copy = await CreateAsync(
            new CreateTemplateRequest(
                name,
                source.Description,
                source.Category,
                TemplateMapper.ParseTags(source.TagsCsv),
                version.WidthMm,
                version.HeightMm,
                version.EditorJson,
                [.. version.Fields.Select(TemplateMapper.ToDto)]),
            cancellationToken);

        if (version.PreviewImageFileId is not null)
        {
            copy.CurrentVersion!.PreviewImageFileId = version.PreviewImageFileId;
            await db.SaveChangesAsync(cancellationToken);
        }

        return copy;
    }

    private static List<TemplateField> ToFieldEntities(TemplateFieldDto[]? fields) =>
        fields is null
            ? []
            : [.. fields.Select(field => new TemplateField
              {
                  Name = field.Name,
                  Label = field.Label,
                  DefaultValue = field.DefaultValue,
                  Required = field.Required
              })];
}
