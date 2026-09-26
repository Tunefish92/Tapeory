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

    [Fact]
    public async Task ExportThenImport_RoundTripsTemplateContent()
    {
        var created = await CreateTemplateAsync();

        var exportResponse = await _client.GetAsync($"/api/templates/{created.Id}/export");
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.Equal("application/json", exportResponse.Content.Headers.ContentType?.MediaType);

        var rawJson = await exportResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"editorJson\"", rawJson);
        Assert.DoesNotContain("\"EditorJson\"", rawJson);

        var exported = JsonSerializer.Deserialize<NativeTemplateExport>(rawJson, JsonOptions);
        Assert.NotNull(exported);

        // Importing the exact same envelope must not collide with the original template's name.
        var importPayload = exported! with { Name = UniqueName("Imported") };
        var importResponse = await _client.PostAsJsonAsync("/api/templates/import", importPayload, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, importResponse.StatusCode);

        var imported = await importResponse.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        Assert.NotNull(imported);
        Assert.Equal(importPayload.Name, imported!.Name);
        Assert.Equal(created.CurrentVersion.WidthMm, imported.CurrentVersion.WidthMm);
        Assert.Equal(created.CurrentVersion.EditorJson, imported.CurrentVersion.EditorJson);
        Assert.Equal(created.CurrentVersion.Fields.Length, imported.CurrentVersion.Fields.Length);
    }

    [Fact]
    public async Task ImportTemplate_ReturnsBadRequest_WhenNameIsNull()
    {
        var export = new NativeTemplateExport(1, null!, null, null, [], 50m, 25m, "{}", []);

        var response = await _client.PostAsJsonAsync("/api/templates/import", export, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
