using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tapeory.Api.Controllers;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class BarcodesControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Encode_ReturnsTheModuleGridAndText()
    {
        var response = await _client.PostAsJsonAsync("/api/barcodes/encode", new EncodeBarcodeRequest("ean13", "400638133393"), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var encoded = await response.Content.ReadFromJsonAsync<EncodedBarcodeResponse>(JsonOptions);
        Assert.Equal((95, 1, false), (encoded!.Columns, encoded.Rows, encoded.TwoDimensional));
        Assert.Equal(95, encoded.Modules.Length);
        Assert.Equal("4006381333931", encoded.Text);
    }

    [Fact]
    public async Task Encode_ReturnsBadRequestWithTheReason_ForAnInvalidValue()
    {
        var response = await _client.PostAsJsonAsync("/api/barcodes/encode", new EncodeBarcodeRequest("ean13", "12"), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("digits", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreatePrintJob_RejectsAFieldValueTheBarcodeCannotEncode()
    {
        var templateResponse = await _client.PostAsJsonAsync(
            "/api/templates",
            new CreateTemplateRequest(
                $"Barcode-{Guid.NewGuid():N}", null, null, null, 40m, 12m,
                """{"objects":[{"type":"barcode","x":1,"y":1,"width":38,"height":10,"rotation":0,"symbology":"ean13","data":"400638133393","fieldName":"gtin","showText":true,"fill":"#000000"}]}""",
                [new TemplateFieldDto("gtin", "GTIN", "400638133393", false)]),
            JsonOptions);
        var template = await templateResponse.Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);

        var valid = await _client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(template!.Id, null, null, [new PrintJobItemRequest(new() { ["gtin"] = "4006381333931" }, 1)]),
            JsonOptions);
        var invalid = await _client.PostAsJsonAsync(
            "/api/print-jobs",
            new CreatePrintJobRequest(template.Id, null, null, [new PrintJobItemRequest(new() { ["gtin"] = "ABC" }, 1)]),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("EAN-13", await invalid.Content.ReadAsStringAsync());
    }
}
