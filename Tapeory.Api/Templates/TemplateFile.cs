using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Templates;

/// <summary>An image inside a template file. <see cref="Id"/> is what the design's image objects
/// refer to ("image-1"); it only means something within that file.</summary>
public sealed record TemplateFileImage(string Id, string FileName, string ContentType, byte[] Content);

/// <summary>What a template file holds, read but not yet stored.</summary>
/// <param name="Document">The design, with image objects pointing at <see cref="Images"/> by
/// their "image" key. Null for a file in the first format, which has
/// <paramref name="LegacyEditorJson"/> instead and no images.</param>
public sealed record TemplateFileContents(
    string? Name,
    string? Description,
    string? Category,
    string[] Tags,
    decimal WidthMm,
    decimal HeightMm,
    JsonObject? Document,
    string? LegacyEditorJson,
    TemplateFieldDto[] Fields,
    IReadOnlyList<TemplateFileImage> Images)
{
    /// <summary>The design as the editor stores it, its images pointing at the uploads they
    /// became (<paramref name="fileIdsByImageId"/>).</summary>
    public string EditorJson(IReadOnlyDictionary<string, int> fileIdsByImageId)
    {
        if (Document is null)
        {
            return LegacyEditorJson ?? string.Empty;
        }

        var document = (JsonObject)Document.DeepClone();

        foreach (var image in TemplateFile.ImageObjects(document))
        {
            if (image["image"] is JsonValue reference
                && reference.TryGetValue<string>(out var id)
                && fileIdsByImageId.TryGetValue(id, out var fileId))
            {
                image.Remove("image");
                image["uploadedFileId"] = fileId;
                image["url"] = $"/api/uploads/images/{fileId}";
            }
        }

        return document.ToJsonString();
    }
}

/// <summary>
/// A template as one file, to download, keep, pass on and upload again: ".tapeory", plain JSON
/// that can be read and compared as text. It holds everything the template needs, so it can be
/// rebuilt on another Tapeory: name, group and description, the label size, the design with all
/// its objects (text, fields, barcodes, shapes), the fields to fill in, and the images, embedded
/// as base64. Fonts are named, not embedded.
///
/// Format 1 (".tapeory.json") had the design as a JSON string inside the JSON and left images
/// out; such files can still be read.
/// </summary>
public static class TemplateFile
{
    public const string FormatName = "tapeory-template";
    public const int FormatVersion = 2;
    public const string Extension = ".tapeory";

    /// <summary>The largest file taken: room for a design with several images at their own limit.</summary>
    public const long MaxSizeBytes = 64 * 1024 * 1024;

    public const int MaxImages = 100;

    private static readonly JsonSerializerOptions Readable = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Umlauts and quotes as themselves, not as \u escapes: the file is meant to be read.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes <paramref name="template"/>'s current version. Image objects whose file is
    /// in <paramref name="imagesByFileId"/> point at it by its id in the file; one whose file is
    /// gone keeps its place in the design without a picture.</summary>
    public static string Write(Template template, IReadOnlyDictionary<int, TemplateFileImage> imagesByFileId, string createdWith)
    {
        var version = template.CurrentVersion
            ?? throw new InvalidOperationException($"Template {template.Id} has no current version loaded.");

        var document = ParseObject(version.EditorJson) ?? new JsonObject
        {
            ["formatVersion"] = 1,
            ["widthMm"] = version.WidthMm,
            ["heightMm"] = version.HeightMm,
            ["objects"] = new JsonArray(),
        };
        var used = new List<TemplateFileImage>();

        foreach (var image in ImageObjects(document))
        {
            var known = image["uploadedFileId"] is JsonValue value
                && value.TryGetValue<int>(out var fileId)
                && imagesByFileId.TryGetValue(fileId, out var found)
                    ? found
                    : null;

            image.Remove("uploadedFileId");
            image.Remove("url");

            if (known is not null)
            {
                image["image"] = known.Id;
                if (!used.Contains(known))
                {
                    used.Add(known);
                }
            }
        }

        var file = new JsonObject
        {
            ["format"] = FormatName,
            ["formatVersion"] = FormatVersion,
            ["createdWith"] = createdWith,
            ["name"] = template.Name,
            ["description"] = template.Description,
            ["group"] = template.Category,
            ["tags"] = new JsonArray([.. TemplateMapper.ParseTags(template.TagsCsv).Select(tag => JsonValue.Create(tag))]),
            ["widthMm"] = version.WidthMm,
            ["heightMm"] = version.HeightMm,
            ["fields"] = JsonSerializer.SerializeToNode(version.Fields.Select(TemplateMapper.ToDto).ToArray(), Readable),
            ["document"] = document,
            ["images"] = new JsonArray([.. used.Select(image => new JsonObject
            {
                ["id"] = image.Id,
                ["fileName"] = image.FileName,
                ["contentType"] = image.ContentType,
                ["data"] = Convert.ToBase64String(image.Content),
            })]),
        };

        return file.ToJsonString(Readable) + "\n";
    }

    /// <summary>Reads a template file of either format. On a file that isn't one, or is damaged,
    /// <paramref name="error"/> says what's wrong in words for the person importing it.</summary>
    public static TemplateFileContents? Read(string text, out string? error)
    {
        error = null;

        if (ParseObject(text) is not { } file)
        {
            error = "This isn't a Tapeory template file.";
            return null;
        }

        var legacyEditorJson = Text(file, "editorJson");
        var document = file["document"] as JsonObject;

        if (document is null && legacyEditorJson is null)
        {
            error = "This isn't a Tapeory template file.";
            return null;
        }

        if (file["formatVersion"] is JsonValue versionValue && versionValue.TryGetValue<int>(out var formatVersion) && formatVersion > FormatVersion)
        {
            error = $"This file was made by a newer Tapeory (file format {formatVersion}). Update Tapeory to import it.";
            return null;
        }

        var images = new List<TemplateFileImage>();

        if (file["images"] is JsonArray entries)
        {
            if (entries.Count > MaxImages)
            {
                error = $"The file has more than {MaxImages} images.";
                return null;
            }

            foreach (var entry in entries.OfType<JsonObject>())
            {
                var id = Text(entry, "id");
                byte[]? content = null;

                try
                {
                    content = Text(entry, "data") is { } data ? Convert.FromBase64String(data) : null;
                }
                catch (FormatException)
                {
                    // reported below
                }

                if (string.IsNullOrEmpty(id) || content is null)
                {
                    error = $"The image {(string.IsNullOrEmpty(id) ? "without an id" : $"'{id}'")} in the file is damaged.";
                    return null;
                }

                images.Add(new TemplateFileImage(id, Text(entry, "fileName") ?? $"{id}.png", Text(entry, "contentType") ?? "image/png", content));
            }
        }

        if (document is not null)
        {
            var missing = ImageObjects(document)
                .Select(image => image["image"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null)
                .FirstOrDefault(id => id is not null && images.All(image => image.Id != id));

            if (missing is not null)
            {
                error = $"The design uses the image '{missing}', which isn't in the file.";
                return null;
            }
        }

        TemplateFieldDto[] fields;

        try
        {
            fields = file["fields"] is JsonArray fieldArray ? fieldArray.Deserialize<TemplateFieldDto[]>(Readable) ?? [] : [];
        }
        catch (JsonException)
        {
            error = "The fields in the file can't be read.";
            return null;
        }

        return new TemplateFileContents(
            Text(file, "name"),
            Text(file, "description"),
            Text(file, "group") ?? Text(file, "category"),
            file["tags"] is JsonArray tags ? [.. tags.Select(tag => tag?.GetValueKind() == JsonValueKind.String ? tag.GetValue<string>() : null).OfType<string>()] : [],
            Number(file, "widthMm"),
            Number(file, "heightMm"),
            document,
            document is null ? legacyEditorJson : null,
            fields,
            images);
    }

    internal static IEnumerable<JsonObject> ImageObjects(JsonObject document) =>
        document["objects"] is JsonArray objects
            ? objects.OfType<JsonObject>().Where(o => o["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name == "image")
            : [];

    private static JsonObject? ParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static decimal Number(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : 0;
}
