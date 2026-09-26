using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tapeory.Api.Backups;
using Tapeory.Api.Controllers;
using Tapeory.Api.Templates;
using Tapeory.Api.Uploads;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class BackupsControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<TemplateDetailResponse> CreateTemplateAsync(string name, string? editorJson = null, TemplateFieldDto[]? fields = null)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest(
                name, "backup test", "Backups", ["a", "b"], 62m, 29m,
                editorJson ?? """{"formatVersion":1,"widthMm":62,"heightMm":29,"objects":[]}""", fields),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions))!;
    }

    private async Task<BackupInfo> CreateBackupAsync(string kind)
    {
        var response = await _client.PostAsync($"/api/backups/{kind}", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BackupInfo>(JsonOptions))!;
    }

    private async Task<RestoreBackupResponse> RestoreAsync(string kind, string fileName)
    {
        var response = await _client.PostAsync($"/api/backups/{kind}/{fileName}/restore", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RestoreBackupResponse>(JsonOptions))!;
    }

    private async Task<List<BackupInfo>> ListAsync(string kind) =>
        (await _client.GetFromJsonAsync<List<BackupInfo>>($"/api/backups/{kind}", JsonOptions))!;

    [Fact]
    public async Task DatabaseBackup_RestoresTheDatabaseToWhenItWasTaken()
    {
        var before = await CreateTemplateAsync($"Before-{Guid.NewGuid():N}");
        var backup = await CreateBackupAsync("database");
        var after = await CreateTemplateAsync($"After-{Guid.NewGuid():N}");

        Assert.True(backup.SizeBytes > 0);
        Assert.False(backup.BeforeRestore);
        Assert.Contains(await ListAsync("database"), b => b.FileName == backup.FileName);

        var download = await _client.GetAsync($"/api/backups/database/{backup.FileName}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Contains("CREATE TABLE", await download.Content.ReadAsStringAsync());

        var restored = await RestoreAsync("database", backup.FileName);

        Assert.Equal(backup.FileName, restored.RestoredFileName);
        Assert.True(restored.SafetyBackup.BeforeRestore);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/templates/{before.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/templates/{after.Id}")).StatusCode);

        // The app keeps working on the restored database.
        await CreateTemplateAsync($"AfterRestore-{Guid.NewGuid():N}");

        // Restoring the safety backup brings the later template back.
        await RestoreAsync("database", restored.SafetyBackup.FileName);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/templates/{after.Id}")).StatusCode);
    }

    [Fact]
    public async Task LabelBackup_RestoresTemplatesWithTheirImages()
    {
        var imageBytes = Encoding.UTF8.GetBytes($"image-{Guid.NewGuid():N}");
        var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(imageBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        upload.Add(file, "file", "logo.png");
        var image = (await (await _client.PostAsync("/api/uploads/images", upload)).Content
            .ReadFromJsonAsync<UploadedImageResponse>(JsonOptions))!;

        var name = $"Labels-{Guid.NewGuid():N}";
        var template = await CreateTemplateAsync(
            name,
            $$"""{"formatVersion":1,"widthMm":62,"heightMm":29,"objects":[{"id":"i","type":"image","x":0,"y":0,"uploadedFileId":{{image.Id}},"url":"/api/uploads/images/{{image.Id}}","width":10,"height":10}]}""",
            [new TemplateFieldDto("customer", "Customer", "ACME", true)]);

        var backup = await CreateBackupAsync("labels");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/templates/{template.Id}")).StatusCode);

        var restored = await RestoreAsync("labels", backup.FileName);

        Assert.True(restored.RestoredTemplates >= 1);
        Assert.True(restored.SafetyBackup.BeforeRestore);

        var summaries = (await _client.GetFromJsonAsync<List<TemplateSummaryResponse>>(
            $"/api/templates?search={name}", JsonOptions))!;
        var summary = Assert.Single(summaries);
        Assert.NotEqual(template.Id, summary.Id);
        Assert.Equal(["a", "b"], summary.Tags);
        Assert.Equal("Backups", summary.Category);

        var detail = (await _client.GetFromJsonAsync<TemplateDetailResponse>($"/api/templates/{summary.Id}", JsonOptions))!;
        var field = Assert.Single(detail.CurrentVersion.Fields);
        Assert.Equal("ACME", field.DefaultValue);

        var restoredImageId = Assert.Single(LabelDocumentImages.FindImageFileIds(detail.CurrentVersion.EditorJson));
        Assert.NotEqual(image.Id, restoredImageId);
        Assert.Contains($"/api/uploads/images/{restoredImageId}", detail.CurrentVersion.EditorJson);
        Assert.Equal(imageBytes, await _client.GetByteArrayAsync($"/api/uploads/images/{restoredImageId}"));
    }

    [Fact]
    public async Task Delete_RemovesTheBackup()
    {
        var backup = await CreateBackupAsync("labels");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/backups/labels/{backup.FileName}")).StatusCode);
        Assert.DoesNotContain(await ListAsync("labels"), b => b.FileName == backup.FileName);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/backups/labels/{backup.FileName}")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/backups/files")]
    [InlineData("GET", "/api/backups/database/tapeory-db-20000101-000000-000.sql")]
    [InlineData("GET", "/api/backups/database/..%2F..%2Fconfig%2Fdatabase.json")]
    [InlineData("POST", "/api/backups/database/tapeory-db-20000101-000000-000.sql/restore")]
    [InlineData("POST", "/api/backups/labels/tapeory-db-20000101-000000-000.sql/restore")]
    [InlineData("DELETE", "/api/backups/labels/secret.txt")]
    public async Task UnknownKindsAndFileNames_AreNotFound(string method, string url)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
