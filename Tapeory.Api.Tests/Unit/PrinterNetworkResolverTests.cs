using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;

namespace Tapeory.Api.Tests.Unit;

public sealed class PrinterNetworkResolverTests
{
    private static Printer Printer(
        PrinterConnectionType type, string? address = null, string? printServerAddress = null, int port = 9100) =>
        new()
        {
            Name = "Test",
            ConnectionType = type,
            Address = address,
            PrintServerAddress = printServerAddress,
            Port = port
        };

    [Fact]
    public void Resolve_UsesAddress_ForIpAddressConnections()
    {
        var target = PrinterNetworkResolver.Resolve(Printer(PrinterConnectionType.IpAddress, address: "192.168.1.50", port: 9100));

        Assert.Equal(new PrinterNetworkTarget("192.168.1.50", 9100), target);
    }

    [Fact]
    public void Resolve_UsesAddress_ForHostnameConnections()
    {
        var target = PrinterNetworkResolver.Resolve(Printer(PrinterConnectionType.Hostname, address: "printer.local", port: 9100));

        Assert.Equal(new PrinterNetworkTarget("printer.local", 9100), target);
    }

    [Fact]
    public void Resolve_UsesPrintServerAddress_ForPrintServerConnections()
    {
        var target = PrinterNetworkResolver.Resolve(
            Printer(PrinterConnectionType.PrintServer, printServerAddress: "printserver.local", port: 9100));

        Assert.Equal(new PrinterNetworkTarget("printserver.local", 9100), target);
    }

    [Fact]
    public void Resolve_ReturnsNull_ForUsbConnections()
    {
        Assert.Null(PrinterNetworkResolver.Resolve(Printer(PrinterConnectionType.Usb)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ReturnsNull_WhenTheRelevantAddressFieldIsBlank(string? address)
    {
        Assert.Null(PrinterNetworkResolver.Resolve(Printer(PrinterConnectionType.IpAddress, address: address)));
    }
}
