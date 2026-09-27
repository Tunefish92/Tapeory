using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace Tapeory.Api.Printing;

/// <summary>What a printer reports about itself over SNMP (standard Host Resources and Printer
/// MIBs, which Brother's network printers implement).</summary>
public sealed record PrinterStatusSnapshot(int DeviceStatus, byte[] ErrorState, string Display, long? LabelCount)
{
    /// <summary>hrPrinterStatus: 3 idle, 4 printing, 5 warming up; 1 "other" usually means an
    /// error the printer is waiting on.</summary>
    public bool IsPrinting => DeviceStatus is 4 or 5;

    /// <summary>A problem that stops printing, or null. The printer's own display text is the
    /// most specific description, so it wins when it says something other than "READY".</summary>
    public string? BlockingError
    {
        get
        {
            var problems = new List<string>();
            var first = ErrorState.Length > 0 ? ErrorState[0] : 0;
            var second = ErrorState.Length > 1 ? ErrorState[1] : 0;

            if ((first & 0x40) != 0 || (second & 0x04) != 0) problems.Add("no tape");
            if ((first & 0x08) != 0) problems.Add("cover open");
            if ((first & 0x04) != 0) problems.Add("tape jam");
            if ((first & 0x02) != 0) problems.Add("offline");
            if ((first & 0x01) != 0) problems.Add("service requested");
            if ((second & 0x20) != 0) problems.Add("tape cassette missing");

            if (problems.Count == 0)
            {
                return null;
            }

            var display = Display.Trim();
            return display.Length > 0 && !display.Equals("READY", StringComparison.OrdinalIgnoreCase)
                ? display
                : string.Join(", ", problems);
        }
    }
}

public interface IPrinterStatusReader
{
    /// <summary>The printer's current status, or null if it doesn't answer SNMP.</summary>
    Task<PrinterStatusSnapshot?> ReadAsync(string host, CancellationToken cancellationToken);
}

public sealed class SnmpPrinterStatusReader(ILogger<SnmpPrinterStatusReader> logger) : IPrinterStatusReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static readonly OctetString Community = new("public");

    private static readonly ObjectIdentifier DeviceStatusOid = new("1.3.6.1.2.1.25.3.5.1.1.1"); // hrPrinterStatus
    private static readonly ObjectIdentifier ErrorStateOid = new("1.3.6.1.2.1.25.3.5.1.2.1"); // hrPrinterDetectedErrorState
    private static readonly ObjectIdentifier DisplayOid = new("1.3.6.1.2.1.43.16.5.1.2.1.1"); // prtConsoleDisplayBufferText
    private static readonly ObjectIdentifier LabelCountOid = new("1.3.6.1.2.1.43.10.2.1.4.1.1"); // prtMarkerLifeCount

    public async Task<PrinterStatusSnapshot?> ReadAsync(string host, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var address = IPAddress.TryParse(host, out var parsed)
                ? parsed
                : (await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, linkedCts.Token)).First();

            var results = await Messenger.GetAsync(
                VersionCode.V2,
                new IPEndPoint(address, 161),
                Community,
                [new(DeviceStatusOid), new(ErrorStateOid), new(DisplayOid), new(LabelCountOid)],
                linkedCts.Token);

            var byId = results.ToDictionary(variable => variable.Id, variable => variable.Data);

            return new PrinterStatusSnapshot(
                byId.GetValueOrDefault(DeviceStatusOid) is Integer32 status ? status.ToInt32() : 0,
                byId.GetValueOrDefault(ErrorStateOid) is OctetString errors ? errors.GetRaw() : [],
                byId.GetValueOrDefault(DisplayOid) is OctetString display ? display.ToString() : "",
                byId.GetValueOrDefault(LabelCountOid) switch
                {
                    Counter32 counter => (long)counter.ToUInt32(),
                    Integer32 integer => integer.ToInt32(),
                    _ => null
                });
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or SnmpException
                                       or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "No SNMP status from printer {Host}.", host);
            return null;
        }
    }
}
