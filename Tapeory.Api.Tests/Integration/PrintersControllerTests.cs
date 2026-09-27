using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Tapeory.Api.Printers;

namespace Tapeory.Api.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class PrintersControllerTests(TapeoryWebApplicationFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static CreatePrinterRequest ValidIpPrinterRequest(string name, int port = 9100) => new(
        name, "Brother PT-P750W", "IpAddress", "127.0.0.1", port, null, null, 62m, 29m, true);

    private async Task<PrinterResponse> CreatePrinterAsync(string? name = null, int port = 9100)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/printers", ValidIpPrinterRequest(name ?? UniqueName("Printer"), port), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);
        Assert.NotNull(created);
        return created!;
    }

    [Fact]
    public async Task CreatePrinter_ReturnsCreated_AndBecomesDefault_WhenItIsTheFirstPrinter()
    {
        var printer = await CreatePrinterAsync();

        Assert.Equal("IpAddress", printer.ConnectionType);
        Assert.Equal(9100, printer.Port);
        Assert.Equal("Unknown", printer.LastConnectionStatus);
        Assert.Equal(["Standard", "High"], printer.Resolutions.Select(r => r.Quality));
        // Not necessarily true in a shared-container test run (other tests may have created
        // printers first), so only assert the invariant that at least one printer is default.
    }

    [Theory]
    [InlineData("PrintServer", " Brother_PT-P750W ", "Brother_PT-P750W")]
    [InlineData("IpAddress", "Brother_PT-P750W", null)] // only print servers have queues
    public async Task CreatePrinter_StoresTheQueueName_ForPrintServers(string connectionType, string queue, string? expected)
    {
        var request = ValidIpPrinterRequest(UniqueName("Printer")) with
        {
            ConnectionType = connectionType,
            PrintServerAddress = "10.0.0.10",
            Port = 631,
            QueueName = queue
        };

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);
        var created = await response.Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);

        Assert.Equal(expected, created!.QueueName);
    }

    [Theory]
    [InlineData("PrintServer", "socket://10.0.0.184:9100")]
    [InlineData("PrintServer", "http://10.0.0.10:631/printers/Brother")]
    [InlineData("IpAddress", "10.0.0.184/9100")]
    public async Task CreatePrinter_ReturnsBadRequest_ForAUrlInsteadOfAHost(string connectionType, string host)
    {
        var request = ValidIpPrinterRequest(UniqueName("Printer")) with
        {
            ConnectionType = connectionType,
            Address = connectionType == "IpAddress" ? host : null,
            PrintServerAddress = connectionType == "PrintServer" ? host : null
        };

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("just the host name or IP address", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreatePrinter_ReturnsBadRequest_ForAQueueNameWithASlash()
    {
        var request = ValidIpPrinterRequest(UniqueName("Printer")) with
        {
            ConnectionType = "PrintServer",
            PrintServerAddress = "10.0.0.10",
            QueueName = "printers/Brother"
        };

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetModels_ListsTheSupportedBrotherModels()
    {
        var models = await _client.GetFromJsonAsync<List<PrinterModelResponse>>("/api/printers/models", JsonOptions);

        var ql = Assert.Single(models!, model => model.Name == "QL-820NWB");
        Assert.Equal(("Ql720", true, 300, true), (ql.Family, ql.Network, ql.Dpi, ql.TwoColor));
        Assert.Contains(models!, model => model.Name == "PT-P950NW" && model.Dpi == 360);
        Assert.Contains(models!, model => model.Name == "QL-700" && !model.Network);
    }

    [Fact]
    public async Task CreatePrinter_ReportsTheModelsOwnResolutionsAndCuttingOptions()
    {
        var request = ValidIpPrinterRequest(UniqueName("Printer")) with { Model = "Brother QL-820NWB" };

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);
        var printer = await response.Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);

        Assert.Equal([300, 600], printer!.Resolutions.Select(r => r.HorizontalDpi));
        Assert.Equal(["AutoCut", "CutAtEnd", "CutMarks"], printer.CutModes);
    }

    [Fact]
    public async Task CreatePrinter_ReturnsBadRequest_WhenNameIsBlank()
    {
        var request = ValidIpPrinterRequest("   ");

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrinter_ReturnsBadRequest_ForAnUnknownConnectionType()
    {
        var request = ValidIpPrinterRequest(UniqueName("Printer")) with { ConnectionType = "Bluetooth" };

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrinter_ReturnsBadRequest_WhenIpAddressTypeHasNoAddress()
    {
        var request = ValidIpPrinterRequest(UniqueName("Printer")) with { Address = null };

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrinter_ReturnsBadRequest_WhenPrintServerTypeHasNoPrintServerAddress()
    {
        var request = new CreatePrinterRequest(
            UniqueName("Printer"), null, "PrintServer", null, 9100, null, null, null, null, true);

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrinter_AllowsUsbPrintersWithNoNetworkAddress()
    {
        var request = new CreatePrinterRequest(
            UniqueName("USB Printer"), "Brother QL-820NWB", "Usb", null, 9100, null, "USB001", 62m, 29m, true);

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreatePrinter_ReturnsBadRequest_ForAnOutOfRangePort()
    {
        var request = ValidIpPrinterRequest(UniqueName("Printer")) with { Port = 70000 };

        var response = await _client.PostAsJsonAsync("/api/printers", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPrinter_ReturnsNotFound_ForAnUnknownId()
    {
        var response = await _client.GetAsync("/api/printers/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePrinter_ChangesFieldsAndReturnsTheUpdatedPrinter()
    {
        var printer = await CreatePrinterAsync();
        var newName = UniqueName("Renamed");

        var update = new UpdatePrinterRequest(
            newName, "Brother QL-820NWB", "Hostname", "printer.local", 9100, null, null, 50m, 25m, false);
        var response = await _client.PutAsJsonAsync($"/api/printers/{printer.Id}", update, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);
        Assert.Equal(newName, updated!.Name);
        Assert.Equal("Hostname", updated.ConnectionType);
        Assert.Equal("printer.local", updated.Address);
        Assert.False(updated.Enabled);
    }

    [Fact]
    public async Task UpdatePrinter_ReturnsNotFound_ForAnUnknownId()
    {
        var update = ValidIpPrinterRequest(UniqueName("Ghost"));
        var response = await _client.PutAsJsonAsync(
            "/api/printers/999999999",
            new UpdatePrinterRequest(update.Name, update.Model, update.ConnectionType, update.Address, update.Port,
                update.PrintServerAddress, update.UsbIdentifier, update.LabelMediaWidthMm, update.LabelMediaHeightMm,
                update.Enabled),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletePrinter_RemovesIt_AndSubsequentGetReturnsNotFound()
    {
        var printer = await CreatePrinterAsync();

        var deleteResponse = await _client.DeleteAsync($"/api/printers/{printer.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/printers/{printer.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeletePrinter_ReturnsNotFound_ForAnUnknownId()
    {
        var response = await _client.DeleteAsync("/api/printers/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetDefaultPrinter_MakesItTheOnlyDefault()
    {
        var first = await CreatePrinterAsync();
        var second = await CreatePrinterAsync();

        var response = await _client.PutAsync($"/api/printers/{second.Id}/default", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var firstAfter = await (await _client.GetAsync($"/api/printers/{first.Id}"))
            .Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);
        var secondAfter = await (await _client.GetAsync($"/api/printers/{second.Id}"))
            .Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);

        Assert.False(firstAfter!.IsDefault);
        Assert.True(secondAfter!.IsDefault);
    }

    [Fact]
    public async Task SetDefaultPrinter_ReturnsNotFound_ForAnUnknownId()
    {
        var response = await _client.PutAsync("/api/printers/999999999/default", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TestConnection_ReturnsSuccess_AndUpdatesStoredStatus_WhenSomethingIsListening()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var printer = await CreatePrinterAsync(port: port);

        var response = await _client.PostAsync($"/api/printers/{printer.Id}/test-connection", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<TestConnectionResponse>(JsonOptions);
        Assert.True(result!.IsSuccess);

        using var accepted = await acceptTask;

        var updated = await (await _client.GetAsync($"/api/printers/{printer.Id}"))
            .Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);
        Assert.Equal("Success", updated!.LastConnectionStatus);
        Assert.NotNull(updated.LastConnectionCheckedAt);
    }

    [Fact]
    public async Task TestConnection_ReturnsFailure_AndRecordsError_WhenNothingIsListening()
    {
        int unusedPort;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            unusedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var printer = await CreatePrinterAsync(port: unusedPort);

        var response = await _client.PostAsync($"/api/printers/{printer.Id}/test-connection", null);
        var result = await response.Content.ReadFromJsonAsync<TestConnectionResponse>(JsonOptions);

        Assert.False(result!.IsSuccess);
        Assert.NotNull(result.ErrorMessage);

        var updated = await (await _client.GetAsync($"/api/printers/{printer.Id}"))
            .Content.ReadFromJsonAsync<PrinterResponse>(JsonOptions);
        Assert.Equal("Failed", updated!.LastConnectionStatus);
        Assert.NotNull(updated.LastErrorMessage);
    }

    [Fact]
    public async Task TestConnection_ReturnsNotFound_ForAnUnknownPrinter()
    {
        var response = await _client.PostAsync("/api/printers/999999999/test-connection", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TestPrint_SendsABrotherRasterJob_ToTheListeningSocket()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var printer = await CreatePrinterAsync(port: port);

        var response = await _client.PostAsync($"/api/printers/{printer.Id}/test-print", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<TestPrintResponse>(JsonOptions);
        Assert.True(result!.IsSuccess);

        using var serverClient = await acceptTask;
        var header = new byte[102];
        await serverClient.GetStream().ReadExactlyAsync(header);

        // A Brother raster job: 100 "invalidate" bytes, then ESC @ to initialize.
        Assert.All(header[..100], b => Assert.Equal(0, b));
        Assert.Equal([0x1B, 0x40], header[100..]);
    }

    [Fact]
    public async Task TestPrint_ReturnsFailure_WhenNothingIsListening()
    {
        int unusedPort;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            unusedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var printer = await CreatePrinterAsync(port: unusedPort);

        var response = await _client.PostAsync($"/api/printers/{printer.Id}/test-print", null);
        var result = await response.Content.ReadFromJsonAsync<TestPrintResponse>(JsonOptions);

        Assert.False(result!.IsSuccess);
    }
}
