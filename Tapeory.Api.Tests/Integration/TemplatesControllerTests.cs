using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class TemplatesControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static CreateTemplateRequest ValidCreateRequest(string name) => new(
        name,
        Description: "A test shipping label",
        Category: "Shipping",
        Tags: ["shipping", "warehouse"],
        WidthMm: 62m,
        HeightMm: 29m,
        EditorJson: """{"objects":[]}""",
        Fields: [new TemplateFieldDto("customerName", "Customer Name", null, true)]);

    private async Task<TemplateDetailResponse> CreateTemplateAsync(string? name = null)
    {
        var request = ValidCreateRequest(name ?? UniqueName("Template"));
        var response = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);
        Assert.NotNull(created);
        return created!;
    }

    [Fact]
    public async Task CreateTemplate_ReturnsCreatedWithDetail_WhenRequestIsValid()
    {
        var name = UniqueName("Template");

        var created = await CreateTemplateAsync(name);

        Assert.Equal(name, created.Name);
        Assert.Equal("Draft", created.Status);
        Assert.Equal(["shipping", "warehouse"], created.Tags);
        Assert.Equal(1, created.CurrentVersion.VersionNumber);
        Assert.Equal(62m, created.CurrentVersion.WidthMm);
        Assert.Single(created.CurrentVersion.Fields);
        Assert.Equal("customerName", created.CurrentVersion.Fields[0].Name);
    }

    [Theory]
    [InlineData(0, 29)]
    [InlineData(62, 0)]
    [InlineData(-1, 29)]
    public async Task CreateTemplate_ReturnsBadRequest_WhenDimensionsAreNotPositive(decimal width, decimal height)
    {
        var request = ValidCreateRequest(UniqueName("Template")) with { WidthMm = width, HeightMm = height };

        var response = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTemplate_ReturnsBadRequest_WhenNameIsBlank()
    {
        var request = ValidCreateRequest("   ");

        var response = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTemplate_ReturnsBadRequest_WhenNameExceedsTheColumnLimit()
    {
        // Regression test: the Name column is varchar(200); before this validation existed, an
        // overlong name would fail the SQL insert with a 500 instead of a clean 400.
        var request = ValidCreateRequest(new string('a', 201));

        var response = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTemplate_Accepts_WhenNameIsExactlyAtTheColumnLimit()
    {
        var request = ValidCreateRequest(new string('a', 200));

        var response = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateTemplate_ReturnsBadRequest_WhenNameIsNull()
    {
        // Regression test: a request body with "name" omitted/null must be rejected with a
        // clean 400, not fall through validation and hit the database's NOT NULL constraint.
        var request = ValidCreateRequest(UniqueName("Template")) with { Name = null! };

        var response = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateTemplate_CreatesADraftCopyOfTheCurrentVersion_AndLeavesTheOriginalAlone()
    {
        var original = await CreateTemplateAsync();
        await _client.PutAsJsonAsync(
            $"/api/templates/{original.Id}",
            new UpdateTemplateMetadataRequest(original.Name, original.Description, original.Category, original.Tags, "Published"),
            JsonOptions);

        var response = await _client.PostAsJsonAsync($"/api/templates/{original.Id}/duplicate", new DuplicateTemplateRequest(null), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<TemplateSummaryResponse>(JsonOptions);

        var copy = await _client.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{summary!.Id}", JsonOptions);
        Assert.NotEqual(original.Id, copy!.Id);
        Assert.Equal($"{original.Name} (copy)", copy.Name);
        Assert.Equal("Draft", copy.Status);
        Assert.Equal(1, copy.CurrentVersion.VersionNumber);
        Assert.Equal(original.Description, copy.Description);
        Assert.Equal(original.Category, copy.Category);
        Assert.Equal(original.Tags, copy.Tags);
        Assert.Equal(original.CurrentVersion.WidthMm, copy.CurrentVersion.WidthMm);
        Assert.Equal(original.CurrentVersion.HeightMm, copy.CurrentVersion.HeightMm);
        Assert.Equal(original.CurrentVersion.EditorJson, copy.CurrentVersion.EditorJson);
        Assert.Equal(original.CurrentVersion.Fields, copy.CurrentVersion.Fields);

        var unchanged = await _client.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{original.Id}", JsonOptions);
        Assert.Equal(original.Name, unchanged!.Name);
        Assert.Equal("Published", unchanged.Status);
    }

    [Fact]
    public async Task DuplicateTemplate_UsesTheGivenName()
    {
        var original = await CreateTemplateAsync();
        var name = UniqueName("Kopie");

        var response = await _client.PostAsJsonAsync($"/api/templates/{original.Id}/duplicate", new DuplicateTemplateRequest(name), JsonOptions);
        var summary = await response.Content.ReadFromJsonAsync<TemplateSummaryResponse>(JsonOptions);

        Assert.Equal(name, summary!.Name);
    }

    [Fact]
    public async Task DuplicateTemplate_ReturnsNotFound_ForUnknownId()
    {
        var response = await _client.PostAsJsonAsync("/api/templates/999999999/duplicate", new DuplicateTemplateRequest(null), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTemplate_ReturnsNotFound_ForUnknownId()
    {
        var response = await _client.GetAsync("/api/templates/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTemplates_IncludesCreatedTemplate_WhenSearchedByName()
    {
        var name = UniqueName("Findable");
        await CreateTemplateAsync(name);

        var response = await _client.GetAsync($"/api/templates?search={Uri.EscapeDataString(name)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<TemplateSummaryResponse[]>(JsonOptions);

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(name, results![0].Name);
    }

    [Fact]
    public async Task UpdateTemplate_UpdatesMetadataAndStatus()
    {
        var created = await CreateTemplateAsync();
        var updatedName = UniqueName("Renamed");

        var update = new UpdateTemplateMetadataRequest(updatedName, "New description", "Retail", ["retail"], "Published");
        var response = await _client.PutAsJsonAsync($"/api/templates/{created.Id}", update, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        Assert.NotNull(detail);
        Assert.Equal(updatedName, detail!.Name);
        Assert.Equal("Published", detail.Status);
        Assert.Equal(["retail"], detail.Tags);
    }

    [Fact]
    public async Task UpdateTemplate_ReturnsBadRequest_ForUnknownStatus()
    {
        var created = await CreateTemplateAsync();

        var update = new UpdateTemplateMetadataRequest(created.Name, null, null, null, "NotAStatus");
        var response = await _client.PutAsJsonAsync($"/api/templates/{created.Id}", update, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTemplate_ReturnsNotFound_ForUnknownId()
    {
        var update = new UpdateTemplateMetadataRequest(UniqueName("Ghost"), null, null, null, null);
        var response = await _client.PutAsJsonAsync("/api/templates/999999999", update, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateVersion_IncrementsVersionNumberAndBecomesCurrent()
    {
        var created = await CreateTemplateAsync();

        var newVersion = new CreateTemplateVersionRequest(
            80m,
            40m,
            """{"objects":["updated"]}""",
            [new TemplateFieldDto("orderNumber", "Order #", null, true)]);

        var response = await _client.PostAsJsonAsync($"/api/templates/{created.Id}/versions", newVersion, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var versionResponse = await response.Content.ReadFromJsonAsync<TemplateVersionResponse>(JsonOptions);
        Assert.NotNull(versionResponse);
        Assert.Equal(2, versionResponse!.VersionNumber);
        Assert.Equal(80m, versionResponse.WidthMm);

        var detailResponse = await _client.GetAsync($"/api/templates/{created.Id}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        Assert.Equal(2, detail!.CurrentVersion.VersionNumber);
        Assert.Equal("Draft", detail.Status);
    }

    [Fact]
    public async Task CreateVersion_ReturnsNotFound_ForUnknownTemplate()
    {
        var newVersion = new CreateTemplateVersionRequest(80m, 40m, "{}", null);

        var response = await _client.PostAsJsonAsync("/api/templates/999999999/versions", newVersion, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetVersions_ReturnsAllVersionsInDescendingOrder()
    {
        var created = await CreateTemplateAsync();

        await _client.PostAsJsonAsync(
            $"/api/templates/{created.Id}/versions",
            new CreateTemplateVersionRequest(80m, 40m, "{}", null),
            JsonOptions);

        var response = await _client.GetAsync($"/api/templates/{created.Id}/versions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var versions = await response.Content.ReadFromJsonAsync<TemplateVersionResponse[]>(JsonOptions);

        Assert.NotNull(versions);
        Assert.Equal(2, versions!.Length);
        Assert.Equal([2, 1], versions.Select(v => v.VersionNumber));
    }

    [Fact]
    public async Task GetVersions_ReturnsNotFound_ForUnknownTemplate()
    {
        var response = await _client.GetAsync("/api/templates/999999999/versions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static StringContent TemplateFileBody(string text) => new(text, System.Text.Encoding.UTF8, "application/json");

    /// <summary>A template with a text, a field, a barcode and an uploaded image.</summary>
    private async Task<(TemplateDetailResponse Template, int ImageId, byte[] ImageBytes)> CreateTemplateWithImageAsync()
    {
        var imageBytes = System.Text.Encoding.UTF8.GetBytes($"png-bytes-{Guid.NewGuid():N}");
        var upload = new MultipartFormDataContent();
        var part = new ByteArrayContent(imageBytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        upload.Add(part, "file", "logo.png");
        var uploaded = await (await _client.PostAsync("/api/uploads/images", upload)).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var imageId = uploaded.GetProperty("id").GetInt32();

        var editorJson = $$"""
            {"formatVersion":1,"widthMm":40,"heightMm":9,"media":"tze-9","objects":[
              {"id":"t1","type":"text","x":1,"y":1,"width":20,"height":7,"text":"Käse \"reif\"","fontSize":10,"fontFamily":"Inter","fontWeight":"bold","align":"left","fill":"#000000","fit":"none"},
              {"id":"f1","type":"dynamicField","x":22,"y":1,"width":8,"height":7,"fieldName":"tag","label":"Tag","defaultValue":"A1","required":true,"fontSize":9,"fontFamily":"Inter","fontWeight":"normal","align":"left","fill":"#000000","fit":"shrink"},
              {"id":"b1","type":"barcode","x":31,"y":1,"width":6.5,"height":6.5,"symbology":"qr","data":"A1","fieldName":"tag","showText":false,"fill":"#000000"},
              {"id":"i1","type":"image","x":1,"y":1,"width":5,"height":5,"uploadedFileId":{{imageId}},"url":"/api/uploads/images/{{imageId}}"}]}
            """;
        var request = new CreateTemplateRequest(
            UniqueName("With image"), "Round trip", "Kitchen", ["a", "b"], 40m, 9m, editorJson,
            [new TemplateFieldDto("tag", "Tag", "A1", true)]);
        var response = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return ((await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!, imageId, imageBytes);
    }

    [Fact]
    public async Task Export_IsAReadableTapeoryFile_WithTheDesignAndItsImagesInside()
    {
        var (created, imageId, imageBytes) = await CreateTemplateWithImageAsync();

        var response = await _client.GetAsync($"/api/templates/{created.Id}/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith(".tapeory", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var text = await response.Content.ReadAsStringAsync();
        // Text someone can read: indented, special characters as themselves.
        Assert.Contains("\n  \"name\": ", text);
        Assert.Contains("Käse \\\"reif\\\"", text);

        using var file = JsonDocument.Parse(text);
        var root = file.RootElement;
        Assert.Equal("tapeory-template", root.GetProperty("format").GetString());
        Assert.Equal(2, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("Kitchen", root.GetProperty("group").GetString());
        Assert.Equal("tag", root.GetProperty("fields")[0].GetProperty("name").GetString());

        // The design is structure, not a string, and its image points into the file, not at this server.
        var objects = root.GetProperty("document").GetProperty("objects");
        Assert.Equal(4, objects.GetArrayLength());
        var image = objects.EnumerateArray().Single(o => o.GetProperty("type").GetString() == "image");
        Assert.False(image.TryGetProperty("uploadedFileId", out _));
        Assert.False(image.TryGetProperty("url", out _));
        var embedded = root.GetProperty("images").EnumerateArray().Single();
        Assert.Equal(image.GetProperty("image").GetString(), embedded.GetProperty("id").GetString());
        Assert.Equal("logo.png", embedded.GetProperty("fileName").GetString());
        Assert.Equal("image/png", embedded.GetProperty("contentType").GetString());
        Assert.Equal(imageBytes, embedded.GetProperty("data").GetBytesFromBase64());
        Assert.DoesNotContain($"/api/uploads/images/{imageId}", text);
    }

    [Fact]
    public async Task ExportThenImport_RebuildsTheTemplate_WithItsOwnCopyOfTheImage()
    {
        var (created, imageId, imageBytes) = await CreateTemplateWithImageAsync();
        var text = await _client.GetStringAsync($"/api/templates/{created.Id}/export");
        var name = UniqueName("Imported");

        var response = await _client.PostAsync("/api/templates/import", TemplateFileBody(text.Replace(created.Name, name)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var imported = (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
        Assert.Equal(name, imported.Name);
        Assert.Equal("Round trip", imported.Description);
        Assert.Equal("Kitchen", imported.Category);
        Assert.Equal(["a", "b"], imported.Tags);
        Assert.Equal(40m, imported.CurrentVersion.WidthMm);
        Assert.Equal(9m, imported.CurrentVersion.HeightMm);
        Assert.Equal(created.CurrentVersion.Fields, imported.CurrentVersion.Fields);

        using var before = JsonDocument.Parse(created.CurrentVersion.EditorJson);
        using var after = JsonDocument.Parse(imported.CurrentVersion.EditorJson);
        var objectsBefore = before.RootElement.GetProperty("objects").EnumerateArray().ToList();
        var objectsAfter = after.RootElement.GetProperty("objects").EnumerateArray().ToList();
        Assert.Equal(objectsBefore.Count, objectsAfter.Count);
        Assert.Equal("tze-9", after.RootElement.GetProperty("media").GetString());

        // Text, field and barcode come back exactly as they were.
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(
                JsonSerializer.Serialize(objectsBefore[i]),
                JsonSerializer.Serialize(objectsAfter[i]));
        }

        // The image is a new upload with the same content.
        var newImageId = objectsAfter[3].GetProperty("uploadedFileId").GetInt32();
        Assert.NotEqual(imageId, newImageId);
        Assert.Equal($"/api/uploads/images/{newImageId}", objectsAfter[3].GetProperty("url").GetString());
        Assert.False(objectsAfter[3].TryGetProperty("image", out _));
        Assert.Equal(imageBytes, await _client.GetByteArrayAsync($"/api/uploads/images/{newImageId}"));
    }

    [Fact]
    public async Task Import_StillTakesFilesInTheFirstFormat()
    {
        var created = await CreateTemplateAsync();
        var legacy = new NativeTemplateExport(
            1, UniqueName("Legacy"), "Old file", "Storage", ["x"], created.CurrentVersion.WidthMm, created.CurrentVersion.HeightMm,
            created.CurrentVersion.EditorJson, created.CurrentVersion.Fields);

        var response = await _client.PostAsJsonAsync("/api/templates/import", legacy, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var imported = (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
        Assert.Equal(legacy.Name, imported.Name);
        Assert.Equal(created.CurrentVersion.EditorJson, imported.CurrentVersion.EditorJson);
        Assert.Equal(created.CurrentVersion.Fields, imported.CurrentVersion.Fields);
    }

    [Theory]
    [InlineData("this is not json", "isn't a Tapeory template file")]
    [InlineData("{\"some\":\"other json\"}", "isn't a Tapeory template file")]
    [InlineData("{\"format\":\"tapeory-template\",\"formatVersion\":99,\"name\":\"x\",\"widthMm\":40,\"heightMm\":9,\"document\":{\"objects\":[]}}", "newer Tapeory")]
    [InlineData("{\"format\":\"tapeory-template\",\"formatVersion\":2,\"name\":\"x\",\"widthMm\":40,\"heightMm\":9,\"document\":{\"objects\":[{\"type\":\"image\",\"image\":\"image-7\"}]},\"images\":[]}", "isn't in the file")]
    [InlineData("{\"format\":\"tapeory-template\",\"formatVersion\":2,\"name\":\"x\",\"widthMm\":40,\"heightMm\":9,\"document\":{\"objects\":[]},\"images\":[{\"id\":\"image-1\",\"data\":\"%%%\"}]}", "is damaged")]
    [InlineData("{\"format\":\"tapeory-template\",\"formatVersion\":2,\"widthMm\":40,\"heightMm\":9,\"document\":{\"objects\":[]}}", "Name is required")]
    [InlineData("{\"format\":\"tapeory-template\",\"formatVersion\":2,\"name\":\"x\",\"widthMm\":0,\"heightMm\":9,\"document\":{\"objects\":[]}}", "greater than zero")]
    public async Task Import_RefusesWhatIsNotAValidTemplateFile_AndSaysWhy(string body, string reason)
    {
        var response = await _client.PostAsync("/api/templates/import", TemplateFileBody(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Import_ChecksEmbeddedImagesLikeUploads_AndCreatesNothingWhenOneIsRefused()
    {
        var name = UniqueName("Bad image");
        var html = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>"));
        var body = $$"""
            {"format":"tapeory-template","formatVersion":2,"name":"{{name}}","widthMm":40,"heightMm":9,
             "document":{"objects":[{"type":"image","x":1,"y":1,"width":5,"height":5,"image":"image-1"}]},
             "images":[{"id":"image-1","fileName":"page.html","contentType":"text/html","data":"{{html}}"}]}
            """;

        var response = await _client.PostAsync("/api/templates/import", TemplateFileBody(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("page.html", await response.Content.ReadAsStringAsync());
        var templates = await _client.GetFromJsonAsync<JsonElement>($"/api/templates?search={name}", JsonOptions);
        Assert.Equal(0, templates.GetArrayLength());
    }

    [Fact]
    public async Task Import_StripsScriptsFromAnEmbeddedSvg()
    {
        var svg = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><script>alert(1)</script><rect width=\"10\" height=\"10\"/></svg>"));
        var body = $$"""
            {"format":"tapeory-template","formatVersion":2,"name":"{{UniqueName("Svg")}}","widthMm":40,"heightMm":9,
             "document":{"objects":[{"type":"image","x":1,"y":1,"width":5,"height":5,"image":"image-1"}]},
             "images":[{"id":"image-1","fileName":"icon.svg","contentType":"image/svg+xml","data":"{{svg}}"}]}
            """;

        var response = await _client.PostAsync("/api/templates/import", TemplateFileBody(body));

        // Either the sanitiser cleans it or it refuses the file; a script never gets stored.
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var imported = (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
            using var document = JsonDocument.Parse(imported.CurrentVersion.EditorJson);
            var id = document.RootElement.GetProperty("objects")[0].GetProperty("uploadedFileId").GetInt32();
            var stored = await _client.GetStringAsync($"/api/uploads/images/{id}");
            Assert.DoesNotContain("<script", stored, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<rect", stored);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task ExportTemplate_ReturnsNotFound_ForAnUnknownTemplate()
    {
        var response = await _client.GetAsync("/api/templates/999999999/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PreviewTemplate_ReturnsAPngSizedToTheTemplatesActualDimensions()
    {
        // Regression test: the renderer must use TemplateVersion.WidthMm/HeightMm (62x29 here),
        // not whatever (or nothing) is embedded in the stored editorJson blob.
        var created = await CreateTemplateAsync();

        var response = await _client.PostAsJsonAsync($"/api/templates/{created.Id}/preview", new { }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);

        const float pxPerMmAt300Dpi = 300f / 25.4f;
        Assert.Equal((int)MathF.Ceiling(62f * pxPerMmAt300Dpi), bitmap.Width);
        Assert.Equal((int)MathF.Ceiling(29f * pxPerMmAt300Dpi), bitmap.Height);
    }

    [Fact]
    public async Task PreviewTemplate_ReturnsAPdf_WhenFormatIsPdf()
    {
        var created = await CreateTemplateAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/templates/{created.Id}/preview", new { format = "pdf" }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task PreviewTemplate_ReturnsNotFound_ForAnUnknownTemplate()
    {
        var response = await _client.PostAsJsonAsync("/api/templates/999999999/preview", new { }, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetPreviewImage_ReturnsNotFound_WhenTemplateDoesNotExist()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/templates/999999999/preview-image",
            new SetPreviewImageRequest(1),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetPreviewImage_ReturnsBadRequest_WhenUploadedFileDoesNotExist()
    {
        var created = await CreateTemplateAsync();

        var response = await _client.PutAsJsonAsync(
            $"/api/templates/{created.Id}/preview-image",
            new SetPreviewImageRequest(999999999),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetThumbnail_ReturnsACacheablePng_ForTheCurrentVersion()
    {
        var created = await CreateTemplateAsync();

        var response = await _client.GetAsync($"/api/templates/{created.Id}/thumbnail?v=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Headers.CacheControl?.MaxAge);

        using var bitmap = SkiaSharp.SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        Assert.NotNull(bitmap);
    }

    [Fact]
    public async Task GetThumbnail_ReturnsNotFound_ForAnUnknownTemplate()
    {
        var response = await _client.GetAsync("/api/templates/999999/thumbnail");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetThumbnail_ReturnsUnprocessable_ForMalformedEditorJson()
    {
        var request = new CreateTemplateRequest(UniqueName("Broken"), null, null, null, 40m, 20m, "not json{{", null);
        var createResponse = await _client.PostAsJsonAsync("/api/templates", request, JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        var response = await _client.GetAsync($"/api/templates/{created!.Id}/thumbnail");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private async Task<TemplateDetailResponse> CreateInGroupAsync(string? group)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/templates", ValidCreateRequest(UniqueName("Grouped")) with { Category = group }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task CreateTemplate_TrimsTheGroup_AndStoresBlankAsNoGroup()
    {
        var trimmed = await CreateInGroupAsync("  Cables  ");
        var blank = await CreateInGroupAsync("   ");

        Assert.Equal("Cables", trimmed.Category);
        Assert.Null(blank.Category);
    }

    [Fact]
    public async Task CreateTemplate_ReturnsBadRequest_WhenTheGroupExceedsTheColumnLimit()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/templates",
            ValidCreateRequest(UniqueName("TooLong")) with { Category = new string('g', TemplateGroups.MaxLength + 1) },
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RenameGroup_MovesEveryTemplateInTheGroup_AndLeavesOthersAlone()
    {
        var group = UniqueName("Jam");
        var renamed = UniqueName("Marmalade");
        var first = await CreateInGroupAsync(group);
        var second = await CreateInGroupAsync(group);
        var other = await CreateInGroupAsync(UniqueName("Cables"));

        var response = await _client.PostAsJsonAsync(
            "/api/templates/groups/rename", new RenameGroupRequest(group, renamed), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RenameGroupResponse>(JsonOptions);
        Assert.Equal(2, result!.UpdatedCount);

        foreach (var id in new[] { first.Id, second.Id })
        {
            var detail = await _client.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{id}", JsonOptions);
            Assert.Equal(renamed, detail!.Category);
        }

        var untouched = await _client.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{other.Id}", JsonOptions);
        Assert.Equal(other.Category, untouched!.Category);
    }

    [Fact]
    public async Task RenameGroup_ToBlank_UngroupsTheTemplates()
    {
        var group = UniqueName("Storage");
        var created = await CreateInGroupAsync(group);

        await _client.PostAsJsonAsync("/api/templates/groups/rename", new RenameGroupRequest(group, ""), JsonOptions);

        var detail = await _client.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{created.Id}", JsonOptions);
        Assert.Null(detail!.Category);
    }

    [Fact]
    public async Task DeleteTemplate_RemovesItFromEverywhere()
    {
        var name = UniqueName("Doomed");
        var created = await CreateTemplateAsync(name);

        var response = await _client.DeleteAsync($"/api/templates/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/templates/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/templates/{created.Id}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/templates/{created.Id}/thumbnail")).StatusCode);

        var list = await _client.GetFromJsonAsync<List<TemplateSummaryResponse>>(
            $"/api/templates?search={Uri.EscapeDataString(name)}", JsonOptions);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task DeleteTemplate_ReturnsNotFound_ForUnknownOrAlreadyDeletedTemplate()
    {
        var created = await CreateTemplateAsync();
        await _client.DeleteAsync($"/api/templates/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/templates/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/templates/999999")).StatusCode);
    }

    [Fact]
    public async Task RenameGroup_ReturnsBadRequest_WithoutASourceGroup()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/templates/groups/rename", new RenameGroupRequest("  ", "Anything"), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
