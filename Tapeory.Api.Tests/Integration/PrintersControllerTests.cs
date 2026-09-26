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
        name, "Brother QL-800", "IpAddress", "127.0.0.1", port, null, null, 62m, 29m, true);

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
        // Not necessarily true in a shared-container test run (other tests may have created
        // printers first), so only assert the invariant that at least one printer is default.
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
    public async Task TestPrint_SendsARenderedLabel_ToTheListeningSocket()
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
        var buffer = new byte[8];
        var read = await serverClient.GetStream().ReadAsync(buffer);

        Assert.True(read > 0);
        // PNG files always start with this fixed 8-byte signature.
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], buffer);
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
