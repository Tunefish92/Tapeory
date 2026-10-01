using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Printers;
using Tapeory.Api.Printing.Usb;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

/// <summary>Printing to a printer on this computer's USB port (a fake port in these tests).</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class UsbPrintingTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<PrinterResponse> CreateUsbPrinterAsync(string device = TapeoryWebApplicationFactory.FakeUsbPort.Device)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/printers",
            new CreatePrinterRequest("Desk P750W", "PT-P750W", "Usb", null, null, null, device, null, null, true),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task ListsThisComputersUsbPrinters()
    {
        var printers = await _client.GetFromJsonAsync<List<UsbPrinterInfo>>("/api/printers/usb", JsonOptions);

        Assert.Equal("Brother PT-P750W", Assert.Single(printers!).Name);
    }

    [Fact]
    public async Task AUsbPrinter_NeedsItsDevice()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/printers",
            new CreatePrinterRequest("No device", "PT-P750W", "Usb", null, null, null, null, null, null, true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PrintsBrotherRasterData_OverUsb()
    {
        var printer = await CreateUsbPrinterAsync();

        try
        {
            var template = await (await _client.PostAsJsonAsync(
                    "/api/templates",
                    new CreateTemplateRequest(
                        $"Usb-{Guid.NewGuid():N}", null, null, null, 40m, 12m,
                        """{"formatVersion":1,"widthMm":40,"heightMm":12,"objects":[]}""", null),
                    JsonOptions))
                .Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);
            var sentBefore = factory.UsbPort.Sent.Count;

            var job = await (await _client.PostAsJsonAsync(
                    "/api/print-jobs",
                    new CreatePrintJobRequest(template!.Id, printer.Id, null, [new PrintJobItemRequest([], 2)]),
                    JsonOptions))
                .Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);

            for (var attempt = 0; attempt < 100 && job!.Status is "Queued" or "Processing" or "Sending" or "Printing"; attempt++)
            {
                await Task.Delay(100);
                job = await _client.GetFromJsonAsync<PrintJobResponse>($"/api/print-jobs/{job.Id}", JsonOptions);
            }

            Assert.Equal("Completed", job!.Status);
            Assert.Equal(sentBefore + 1, factory.UsbPort.Sent.Count);

            // Brother raster: the invalidate NULs, then ESC @ (initialize).
            var data = factory.UsbPort.Sent[^1];
            var firstCommand = Array.FindIndex(data, value => value != 0);
            Assert.True(firstCommand >= 100);
            Assert.Equal(new byte[] { 0x1B, 0x40 }, data[firstCommand..(firstCommand + 2)]);
        }
        finally
        {
            await _client.DeleteAsync($"/api/printers/{printer.Id}");
        }
    }

    [Fact]
    public async Task TestConnection_ChecksThePrinterIsConnected()
    {
        var connected = await CreateUsbPrinterAsync();
        var unplugged = await CreateUsbPrinterAsync("/dev/usb/lp7");

        try
        {
            var ok = await (await _client.PostAsync($"/api/printers/{connected.Id}/test-connection", null))
                .Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            var missing = await (await _client.PostAsync($"/api/printers/{unplugged.Id}/test-connection", null))
                .Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

            Assert.True(ok.GetProperty("isSuccess").GetBoolean());
            Assert.False(missing.GetProperty("isSuccess").GetBoolean());
            Assert.Contains("lp7", missing.GetProperty("errorMessage").GetString());
        }
        finally
        {
            await _client.DeleteAsync($"/api/printers/{connected.Id}");
            await _client.DeleteAsync($"/api/printers/{unplugged.Id}");
        }
    }
}
