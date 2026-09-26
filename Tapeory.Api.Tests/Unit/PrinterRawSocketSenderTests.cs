using System.Net;
using System.Net.Sockets;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;

namespace Tapeory.Api.Tests.Unit;

public sealed class PrinterRawSocketSenderTests
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
    public async Task SendAsync_TransmitsTheExactBytes_WhenSomethingIsListening()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        byte[] payload = [1, 2, 3, 4, 5, 255, 0, 128];

        var sendTask = new PrinterRawSocketSender().SendAsync(NetworkPrinter(port), payload, CancellationToken.None);

        using var serverClient = await acceptTask;
        var received = await ReadExactlyAsync(serverClient.GetStream(), payload.Length);

        var result = await sendTask;

        Assert.True(result.IsSuccess);
        Assert.Equal(payload, received);
    }

    [Fact]
    public async Task SendAsync_ReturnsFailure_WhenNothingIsListening()
    {
        var result = await new PrinterRawSocketSender().SendAsync(
            NetworkPrinter(GetUnusedPort()), [1, 2, 3], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_ReturnsFailure_ForUsbPrinters_WithoutAttemptingAnyIO()
    {
        var printer = new Printer { Name = "USB Printer", ConnectionType = PrinterConnectionType.Usb };

        var result = await new PrinterRawSocketSender().SendAsync(printer, [1, 2, 3], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("USB", result.ErrorMessage);
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int length)
    {
        var buffer = new byte[length];
        var totalRead = 0;

        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead));

            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return buffer;
    }
}
