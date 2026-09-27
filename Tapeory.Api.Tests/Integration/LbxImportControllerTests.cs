using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class LbxImportControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private const string LabelXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <pt:document xmlns:pt="http://schemas.brother.info/ptouch/2007/lbx/main"
                      xmlns:style="http://schemas.brother.info/ptouch/2007/lbx/style"
                      xmlns:text="http://schemas.brother.info/ptouch/2007/lbx/text">
          <pt:body>
            <style:sheet>
              <style:paper width="175.7pt" height="319.8pt"/>
              <pt:objects>
                <text:text>
                  <pt:objectStyle x="6.0pt" y="10.0pt" width="100.0pt" height="30.0pt" angle="0">
                    <pt:expanded objectName="Text1" dbMergeFieldStyleName=""/>
                  </pt:objectStyle>
                  <text:ptFontInfo>
                    <text:logFont name="Arial" weight="400"/>
                    <text:fontExt size="12.0pt" textColor="#000000"/>
                  </text:ptFontInfo>
                  <text:textAlign horizontalAlignment="LEFT"/>
                  <text:stringItem/>
                  <pt:data>Hello from a test fixture</pt:data>
                </text:text>
              </pt:objects>
            </style:sheet>
          </pt:body>
        </pt:document>
        """;

    private const string PropXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <meta:properties xmlns:meta="http://schemas.brother.info/ptouch/2007/lbx/meta"
                          xmlns:dc="http://purl.org/dc/elements/1.1/">
          <dc:title>Fixture Label</dc:title>
        </meta:properties>
        """;

    private static byte[] BuildValidLbxBytes()
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "label.xml", LabelXml);
            WriteEntry(archive, "prop.xml", PropXml);
        }

        return stream.ToArray();
    }

    private static byte[] BuildLbxBytesWithTitle(string title)
    {
        var propXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <meta:properties xmlns:meta="http://schemas.brother.info/ptouch/2007/lbx/meta"
                              xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>{title}</dc:title>
            </meta:properties>
            """;

        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "label.xml", LabelXml);
            WriteEntry(archive, "prop.xml", propXml);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static MultipartFormDataContent BuildUpload(byte[] bytes, string fileName)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task ImportLbx_CreatesATemplate_WithTextConvertedAndNamePulledFromPropXml()
    {
        var response = await _client.PostAsync("/api/templates/import-lbx", BuildUpload(BuildValidLbxBytes(), "fixture.lbx"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        Assert.NotNull(detail);
        Assert.Equal("Fixture Label", detail!.Name);
        Assert.NotNull(detail.SourceLbxUrl);
        Assert.Empty(detail.ConversionWarnings);
        Assert.Contains("Hello from a test fixture", detail.CurrentVersion.EditorJson);
    }

    [Fact]
    public async Task ImportLbx_ConvertsEmbeddedBmpAndTiffImages_ToPngUploads()
    {
        const string labelXml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <pt:document xmlns:pt="http://schemas.brother.info/ptouch/2007/lbx/main"
                          xmlns:style="http://schemas.brother.info/ptouch/2007/lbx/style"
                          xmlns:image="http://schemas.brother.info/ptouch/2007/lbx/image">
              <pt:body>
                <style:sheet>
                  <style:paper width="68pt" height="200pt"/>
                  <pt:objects>
                    <image:image>
                      <pt:objectStyle x="6.8pt" y="25.3pt" width="13.5pt" height="17pt" angle="0">
                        <pt:expanded objectName="Bitmap9"/>
                      </pt:objectStyle>
                      <image:imageStyle originalName="3005.png" fileName="Object0.bmp"/>
                    </image:image>
                    <image:image>
                      <pt:objectStyle x="27pt" y="8.3pt" width="101.5pt" height="51.2pt" angle="0">
                        <pt:expanded objectName="Bitmap10"/>
                      </pt:objectStyle>
                      <image:imageStyle fileName="Object1.tif"/>
                    </image:image>
                    <image:image>
                      <pt:objectStyle x="0pt" y="0pt" width="10pt" height="10pt" angle="0">
                        <pt:expanded objectName="Bitmap11"/>
                      </pt:objectStyle>
                      <image:imageStyle fileName="Missing.bmp"/>
                    </image:image>
                  </pt:objects>
                </style:sheet>
              </pt:body>
            </pt:document>
            """;

        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "label.xml", labelXml);
            archive.CreateEntry("Object0.bmp").Open().Using(s => s.Write(Unit.TestImages.Bmp()));
            archive.CreateEntry("Object1.tif").Open().Using(s => s.Write(Unit.TestImages.BilevelTiff()));
        }

        var response = await _client.PostAsync("/api/templates/import-lbx", BuildUpload(stream.ToArray(), "images.lbx"));
        var detail = await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        using var editor = JsonDocument.Parse(detail!.CurrentVersion.EditorJson);
        var images = editor.RootElement.GetProperty("objects").EnumerateArray().ToList();
        Assert.Equal(2, images.Count);
        Assert.All(images, image => Assert.Equal("image", image.GetProperty("type").GetString()));

        foreach (var image in images)
        {
            var id = image.GetProperty("uploadedFileId").GetInt32();
            Assert.Equal($"/api/uploads/images/{id}", image.GetProperty("url").GetString());

            var png = await _client.GetByteArrayAsync($"/api/uploads/images/{id}");
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        }

        var warning = Assert.Single(detail.ConversionWarnings);
        Assert.Contains("Bitmap11", warning);
    }

    [Fact]
    public async Task ImportLbx_ReturnsBadRequest_ForANonLbxExtension()
    {
        var response = await _client.PostAsync(
            "/api/templates/import-lbx", BuildUpload([1, 2, 3], "not-a-label.txt"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportLbx_ReturnsBadRequest_WhenNoFileProvided()
    {
        var response = await _client.PostAsync("/api/templates/import-lbx", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportLbx_StillSucceeds_ForACorruptArchive_PreservingTheOriginalWithAWarning()
    {
        var corruptBytes = Encoding.UTF8.GetBytes("this is not actually a zip file");

        var response = await _client.PostAsync("/api/templates/import-lbx", BuildUpload(corruptBytes, "corrupt.lbx"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        Assert.NotNull(detail);
        Assert.NotEmpty(detail!.ConversionWarnings);
        Assert.NotNull(detail.SourceLbxUrl);

        // The original bytes must still be downloadable even though parsing failed entirely.
        var download = await _client.GetAsync(detail.SourceLbxUrl);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(corruptBytes, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ImportLbx_TruncatesAnOverlongTitleFromPropXml_InsteadOfFailing()
    {
        // Regression test: the Name column is varchar(200). There's no form to reject this back
        // to (the name comes from prop.xml, not a user-typed field), so an overlong title should
        // be truncated rather than causing a 500 on save.
        var bytes = BuildLbxBytesWithTitle(new string('a', 250));

        var response = await _client.PostAsync("/api/templates/import-lbx", BuildUpload(bytes, "fixture.lbx"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);
        Assert.NotNull(detail);
        Assert.True(detail!.Name.Length <= 200);
    }

    [Fact]
    public async Task ImportLbx_TruncatesAnOverlongFilename_WhenPropXmlHasNoTitle()
    {
        var bytesWithoutTitle = BuildLbxBytesWithTitle(string.Empty);
        var longFileName = new string('a', 250) + ".lbx";

        var response = await _client.PostAsync(
            "/api/templates/import-lbx", BuildUpload(bytesWithoutTitle, longFileName));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);
        Assert.NotNull(detail);
        Assert.True(detail!.Name.Length <= 200);
    }

    [Fact]
    public async Task DownloadOriginalLbx_ReturnsTheExactBytesThatWereUploaded()
    {
        var bytes = BuildValidLbxBytes();
        var importResponse = await _client.PostAsync("/api/templates/import-lbx", BuildUpload(bytes, "roundtrip.lbx"));
        var detail = await importResponse.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        var download = await _client.GetAsync(detail!.SourceLbxUrl);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DownloadOriginalLbx_ReturnsNotFound_ForATemplateWithNoSourceFile()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest($"Native-{Guid.NewGuid():N}", null, null, null, 50m, 25m, "{}", null),
            JsonOptions);
        var detail = await created.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        var response = await _client.GetAsync($"/api/templates/{detail!.Id}/original-lbx");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadOriginalLbx_ReturnsNotFound_ForAnUnknownTemplateId()
    {
        var response = await _client.GetAsync("/api/templates/999999999/original-lbx");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

internal static class StreamTestExtensions
{
    public static void Using(this Stream stream, Action<Stream> write)
    {
        using (stream)
        {
            write(stream);
        }
    }
}
