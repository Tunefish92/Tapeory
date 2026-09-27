using System.Net;
using System.Net.Sockets;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;

namespace Tapeory.Api.Tests.Unit;

public sealed class PrinterConnectionTesterTests
{
    private static Printer NetworkPrinter(int port) => new()
    {
        Name = "Test",
        ConnectionType = PrinterConnectionType.IpAddress,
        Address = "127.0.0.1",
        Port = port
    };

    private static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task TestAsync_ReturnsSuccess_WhenSomethingIsListening()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var result = await new PrinterConnectionTester(new IppClient(new HttpClient())).TestAsync(NetworkPrinter(port), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);

        using var accepted = await acceptTask;
    }

    [Fact]
    public async Task TestAsync_ReturnsFailure_WhenNothingIsListening()
    {
        var result = await new PrinterConnectionTester(new IppClient(new HttpClient())).TestAsync(NetworkPrinter(GetUnusedPort()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task TestAsync_ReturnsFailure_ForUsbPrinters_WithoutAttemptingAnyIO()
    {
        var printer = new Printer { Name = "USB Printer", ConnectionType = PrinterConnectionType.Usb };

        var result = await new PrinterConnectionTester(new IppClient(new HttpClient())).TestAsync(printer, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("USB", result.ErrorMessage);
    }

    [Fact]
    public async Task TestAsync_ReturnsFailure_WhenNoAddressIsConfigured()
    {
        var printer = new Printer { Name = "Test", ConnectionType = PrinterConnectionType.IpAddress, Address = null };

        var result = await new PrinterConnectionTester(new IppClient(new HttpClient())).TestAsync(printer, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("address", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static Printer CupsQueue() => new()
    {
        Name = "Via CUPS",
        ConnectionType = PrinterConnectionType.PrintServer,
        PrintServerAddress = "cups.example",
        Port = 631,
        QueueName = "Brother_PT-P750W"
    };

    [Fact]
    public async Task TestAsync_AsksThePrintServerForTheQueue_WhenAQueueNameIsSet()
    {
        var cups = new FakeIppServer(IppResponses.Printer(state: 3, acceptingJobs: true));

        var result = await new PrinterConnectionTester(new IppClient(new HttpClient(cups)))
            .TestAsync(CupsQueue(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("http://cups.example:631/printers/Brother_PT-P750W", cups.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task TestAsync_ReportsThePrintServersMessage_WhenTheQueueDoesNotExist()
    {
        var cups = new FakeIppServer(IppResponses.Error(0x0406, "The printer or class does not exist."));

        var result = await new PrinterConnectionTester(new IppClient(new HttpClient(cups)))
            .TestAsync(CupsQueue(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("does not exist", result.ErrorMessage);
    }
}
