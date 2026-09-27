using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Backups;

/// <summary>
/// "backup.json" inside a label backup archive. Files are stored next to it under
/// <see cref="LabelBackupFile.ArchivePath"/> and keep the id they had when the backup was made,
/// which is what the templates' editor JSON and preview/.lbx references point at.
/// </summary>
public sealed record LabelBackupContents(
    int FormatVersion,
    DateTimeOffset CreatedAt,
    List<LabelBackupTemplate> Templates,
    List<LabelBackupFile> Files)
{
    public const int CurrentFormatVersion = 1;
    public const string EntryName = "backup.json";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>A template's metadata and current version. Version history isn't included; the
/// database backup has that.</summary>
public sealed record LabelBackupTemplate(
    string Name,
    string? Description,
    string? Category,
    string[] Tags,
    TemplateStatus Status,
    decimal WidthMm,
    decimal HeightMm,
    string EditorJson,
    TemplateFieldDto[] Fields,
    int? PreviewImageFileId,
    int? SourceLbxFileId,
    string[] ConversionWarnings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Added with accounts; backups made before them have neither, and restore as shared.
    string? OwnerUserName = null,
    bool? IsPublic = null);

public sealed record LabelBackupFile(
    int Id,
    string OriginalFileName,
    string ContentType,
    FileStorageCategory Category,
    string ArchivePath);

/// <summary>Finds and rewrites the uploaded-image references inside a template's editor JSON.</summary>
public static class LabelDocumentImages
{
    public static IReadOnlyCollection<int> FindImageFileIds(string editorJson)
    {
        var ids = new HashSet<int>();

        foreach (var image in ImageObjects(editorJson))
        {
            if (TryGetFileId(image, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Points image objects at the ids their files got when restored. Images whose file isn't in
    /// <paramref name="newIdsByOldId"/> are left alone, as is JSON that can't be parsed — the
    /// editor and renderer already cope with both.
    /// </summary>
    public static string RemapImageFileIds(string editorJson, IReadOnlyDictionary<int, int> newIdsByOldId)
    {
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(editorJson);
        }
        catch (JsonException)
        {
            return editorJson;
        }

        var changed = false;

        foreach (var image in ImageObjects(root))
        {
            if (TryGetFileId(image, out var oldId) && newIdsByOldId.TryGetValue(oldId, out var newId))
            {
                image["uploadedFileId"] = newId;
                image["url"] = $"/api/uploads/images/{newId}";
                changed = true;
            }
        }

        return changed ? root!.ToJsonString() : editorJson;
    }

    private static IEnumerable<JsonObject> ImageObjects(string editorJson)
    {
        try
        {
            return ImageObjects(JsonNode.Parse(editorJson));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<JsonObject> ImageObjects(JsonNode? root) =>
        root is JsonObject document && document["objects"] is JsonArray objects
            ? objects.OfType<JsonObject>().Where(o => o["type"] is JsonValue type && type.TryGetValue<string>(out var t) && t == "image")
            : [];

    private static bool TryGetFileId(JsonObject image, out int id)
    {
        id = 0;
        return image["uploadedFileId"] is JsonValue value && value.TryGetValue(out id);
    }
}
