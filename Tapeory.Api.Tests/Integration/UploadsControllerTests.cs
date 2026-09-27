using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tapeory.Api.Templates;
using Tapeory.Api.Uploads;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class UploadsControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private static MultipartFormDataContent BuildImageUpload(
        byte[] bytes,
        string fileName = "logo.png",
        string contentType = "image/png")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task UploadImage_ReturnsCreated_ForValidPng()
    {
        var bytes = Encoding.UTF8.GetBytes("not-really-a-png-but-thats-fine-for-this-test");

        var response = await _client.PostAsync("/api/uploads/images", BuildImageUpload(bytes));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var uploaded = await response.Content.ReadFromJsonAsync<UploadedImageResponse>(JsonOptions);

        Assert.NotNull(uploaded);
        Assert.Equal("logo.png", uploaded!.OriginalFileName);
        Assert.Equal("image/png", uploaded.ContentType);
        Assert.Equal(bytes.Length, uploaded.SizeBytes);
        Assert.Equal($"/api/uploads/images/{uploaded.Id}", uploaded.Url);
        Assert.NotNull(response.Headers.Location);
    }

    [Theory]
    [InlineData("artwork.tif", "image/tiff")]
    [InlineData("artwork.bmp", "image/bmp")]
    public async Task UploadImage_StoresTiffAndBmpAsPng(string fileName, string contentType)
    {
        var bytes = contentType == "image/tiff" ? Unit.TestImages.BilevelTiff() : Unit.TestImages.Bmp();

        var response = await _client.PostAsync("/api/uploads/images", BuildImageUpload(bytes, fileName, contentType));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var uploaded = await response.Content.ReadFromJsonAsync<UploadedImageResponse>(JsonOptions);
        Assert.Equal("image/png", uploaded!.ContentType);
        Assert.Equal(Path.ChangeExtension(fileName, ".png"), uploaded.OriginalFileName);

        var stored = await _client.GetByteArrayAsync(uploaded.Url);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], stored[..4]);
    }

    [Fact]
    public async Task UploadImage_ReturnsBadRequest_ForATiffThatCannotBeRead()
    {
        var response = await _client.PostAsync(
            "/api/uploads/images", BuildImageUpload([(byte)'I', (byte)'I', 42, 0, 1, 2, 3], "broken.tif", "image/tiff"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadImage_ReturnsBadRequest_ForDisallowedContentType()
    {
        var bytes = Encoding.UTF8.GetBytes("<html>not an image</html>");

        var response = await _client.PostAsync(
            "/api/uploads/images",
            BuildImageUpload(bytes, "page.html", "text/html"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadImage_ReturnsBadRequest_WhenNoFileProvided()
    {
        var content = new MultipartFormDataContent();

        var response = await _client.PostAsync("/api/uploads/images", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetImage_ReturnsNotFound_ForUnknownId()
    {
        var response = await _client.GetAsync("/api/uploads/images/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetImage_ReturnsTheExactBytesThatWereUploaded()
    {
        var bytes = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString());

        var uploadResponse = await _client.PostAsync("/api/uploads/images", BuildImageUpload(bytes));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadedImageResponse>(JsonOptions);

        var downloadResponse = await _client.GetAsync(uploaded!.Url);
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);

        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, downloadedBytes);
    }

    [Fact]
    public async Task UploadedImage_CanBeAttachedAsTemplatePreviewImage()
    {
        var bytes = Encoding.UTF8.GetBytes("preview-image-bytes");
        var uploadResponse = await _client.PostAsync("/api/uploads/images", BuildImageUpload(bytes));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadedImageResponse>(JsonOptions);

        var createRequest = new CreateTemplateRequest(
            $"PreviewTest-{Guid.NewGuid():N}",
            null,
            null,
            null,
            50m,
            25m,
            "{}",
            null);
        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest, JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        var setPreviewResponse = await _client.PutAsJsonAsync(
            $"/api/templates/{created!.Id}/preview-image",
            new SetPreviewImageRequest(uploaded!.Id),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, setPreviewResponse.StatusCode);

        var detailResponse = await _client.GetAsync($"/api/templates/{created.Id}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        Assert.Equal(uploaded.Url, detail!.CurrentVersion.PreviewImageUrl);
    }

    [Fact]
    public async Task UploadImage_StripsScriptTagsFromAnUploadedSvg()
    {
        var svg = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert('xss')</script>" +
            "<rect width=\"10\" height=\"10\" onload=\"alert(1)\"/></svg>");

        var uploadResponse = await _client.PostAsync(
            "/api/uploads/images", BuildImageUpload(svg, "icon.svg", "image/svg+xml"));

        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadedImageResponse>(JsonOptions);

        var downloadResponse = await _client.GetAsync(uploaded!.Url);
        var downloaded = await downloadResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("script", downloaded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", downloaded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rect", downloaded);
    }

    [Fact]
    public async Task UploadImage_ReturnsBadRequest_ForAnSvgContentTypeFileThatIsNotValidXml()
    {
        var bytes = Encoding.UTF8.GetBytes("this claims to be svg but is not xml");

        var response = await _client.PostAsync(
            "/api/uploads/images", BuildImageUpload(bytes, "icon.svg", "image/svg+xml"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
