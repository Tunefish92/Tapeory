using System.Xml.Linq;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Storage;

namespace Tapeory.Api.Import;

public sealed class LbxImportService(AppDbContext db, FileStorageService fileStorage)
{
    /// <summary>Imports a .lbx upload: the original bytes are always stored first and preserved
    /// unchanged, regardless of whether the file could be parsed — so no source data is ever
    /// lost even for a file this importer can't make sense of.</summary>
    public async Task<Template> ImportAsync(
        Stream fileStream,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var stored = await fileStorage.SaveAsync(
            buffer, originalFileName, FileStorageCategory.OriginalLbx, cancellationToken);

        var sourceFile = new UploadedFile
        {
            FileName = stored.FileName,
            OriginalFileName = originalFileName,
            ContentType = "application/octet-stream",
            SizeBytes = stored.SizeBytes,
            Category = FileStorageCategory.OriginalLbx,
            RelativePath = stored.RelativePath
        };

        db.UploadedFiles.Add(sourceFile);

        buffer.Position = 0;
        var (conversionResult, suggestedName) = ParseSafely(buffer, originalFileName);

        var template = new Template
        {
            Name = suggestedName,
            SourceLbxFile = sourceFile
        };

        var version = new TemplateVersion
        {
            VersionNumber = 1,
            WidthMm = conversionResult.WidthMm,
            HeightMm = conversionResult.HeightMm,
            EditorJson = conversionResult.EditorJson,
            Fields = [.. conversionResult.Fields.Select(field => new TemplateField
            {
                Name = field.Name,
                Label = field.Label,
                DefaultValue = field.DefaultValue,
                Required = field.Required
            })]
        };

        template.Versions.Add(version);

        foreach (var warning in conversionResult.Warnings)
        {
            template.ConversionWarnings.Add(new TemplateConversionWarning { Message = warning });
        }

        db.Templates.Add(template);
        // See TemplateService.CreateAsync for why this needs two SaveChanges calls: Template and
        // TemplateVersion are both brand-new rows that reference each other by generated key.
        await db.SaveChangesAsync(cancellationToken);

        template.CurrentVersion = version;
        await db.SaveChangesAsync(cancellationToken);

        return template;
    }

    // Must match the Template.Name column's HasMaxLength(200) in AppDbContext. This name is
    // system-derived (from prop.xml's title or the uploaded filename), not typed by a user, so
    // truncating silently is the right behavior — there's no form to reject it back to.
    private const int MaxNameLength = 200;

    private static (LbxConversionResult Result, string SuggestedName) ParseSafely(
        Stream zipStream, string originalFileName)
    {
        var fallbackName = Truncate(Path.GetFileNameWithoutExtension(originalFileName));

        if (string.IsNullOrWhiteSpace(fallbackName))
        {
            fallbackName = "Imported Template";
        }

        try
        {
            var archive = LbxArchiveReader.Read(zipStream);
            var result = LbxObjectConverter.Convert(archive.LabelXml);
            var name = Truncate(ReadTitleFromProp(archive.PropXml) ?? fallbackName);
            return (result, name);
        }
        catch (LbxParseException ex)
        {
            return (LbxObjectConverter.EmptyResult(ex.Message), fallbackName);
        }
    }

    private static string Truncate(string value) =>
        value.Length > MaxNameLength ? value[..MaxNameLength] : value;

    private static string? ReadTitleFromProp(XDocument? propXml)
    {
        if (propXml?.Root is null)
        {
            return null;
        }

        var title = propXml.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value;
        return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
    }
}
