using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Printing;

public sealed record PrinterNetworkTarget(string Host, int Port);

/// <summary>Resolves a Printer's configured connection info down to a plain host:port for the
/// raw-socket operations (connection test, raw print send) that work identically regardless of
/// connection type — everything except USB is, from this app's point of view, just a network
/// address.</summary>
public static class PrinterNetworkResolver
{
    public static PrinterNetworkTarget? Resolve(Printer printer)
    {
        var host = printer.ConnectionType switch
        {
            PrinterConnectionType.IpAddress => printer.Address,
            PrinterConnectionType.Hostname => printer.Address,
            PrinterConnectionType.PrintServer => printer.PrintServerAddress,
            _ => null
        };

        return string.IsNullOrWhiteSpace(host) ? null : new PrinterNetworkTarget(host, printer.Port);
    }
}
