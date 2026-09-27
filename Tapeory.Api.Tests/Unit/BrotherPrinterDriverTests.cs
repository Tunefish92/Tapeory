using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;

namespace Tapeory.Api.Tests.Unit;

public sealed class BrotherPrinterDriverTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly SKBitmap _label = new(4, 64);

    public BrotherPrinterDriverTests()
    {
        _listener.Start();
        _label.Erase(SKColors.White);
        _ = DrainConnectionsAsync();
    }

    public void Dispose()
    {
        _listener.Stop();
        _label.Dispose();
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Accepts and reads every connection, like a printer that takes any job.</summary>
    private async Task DrainConnectionsAsync()
    {
        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await client.GetStream().CopyToAsync(Stream.Null);
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // listener stopped
        }
    }

    private Printer PtPrinter() => new()
    {
        Name = "Test",
        Model = "Brother PT-P750W",
        ConnectionType = PrinterConnectionType.IpAddress,
        Address = "127.0.0.1",
        Port = Port
    };

    private static PrinterStatusSnapshot Idle(long count) => new(3, [0x00], "READY", count);

    private static PrinterStatusSnapshot Busy(long count) => new(4, [0x00], "PRINTING", count);

    /// <summary>Answers each status read with the next snapshot in line, repeating the last one.</summary>
    private sealed class ScriptedStatusReader(params PrinterStatusSnapshot?[] snapshots) : IPrinterStatusReader
    {
        private int _next;

        public Task<PrinterStatusSnapshot?> ReadAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(snapshots[Math.Min(_next++, snapshots.Length - 1)]);
    }

    private static BrotherPrinterDriver Driver(IPrinterStatusReader status, IppClient? ipp = null) =>
        new(new PrinterRawSocketSender(), status, ipp ?? new IppClient(new HttpClient()), NullLogger<BrotherPrinterDriver>.Instance)
        {
            PollInterval = TimeSpan.FromMilliseconds(1),
            BaseTimeout = TimeSpan.FromSeconds(2),
            TimeoutPerLabel = TimeSpan.Zero
        };

    private async Task<(PrintOutcome Outcome, List<PrintStage> Stages)> PrintAsync(
        IPrinterStatusReader status, int copies = 1, Printer? printer = null, IppClient? ipp = null)
    {
        var stages = new List<PrintStage>();
        var outcome = await Driver(status, ipp).PrintAsync(
            printer ?? PtPrinter(),
            Enumerable.Repeat(_label, copies).ToList(),
            BrotherCatalog.Find("PT-P750W").Media.Single(media => media.Id == "tze-9"),
            PrinterCapabilities.Standard,
            CutMode.AutoCut,
            stage =>
            {
                stages.Add(stage);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        return (outcome, stages);
    }

    [Fact]
    public async Task Print_IsConfirmed_OnceTheLabelCounterRisesByTheCopiesAndThePrinterIsIdle()
    {
        var (outcome, stages) = await PrintAsync(
            new ScriptedStatusReader(Idle(100), Busy(100), Busy(101), Idle(102)), copies: 2);

        Assert.True(outcome.IsSuccess);
        Assert.True(outcome.Confirmed);
        Assert.Equal([PrintStage.Sending, PrintStage.Printing], stages);
    }

    [Fact]
    public async Task Print_Fails_WhenThePrinterStopsWithAnError()
    {
        var noTape = new PrinterStatusSnapshot(1, [0x40], "NO TAPE", 100);

        var (outcome, _) = await PrintAsync(new ScriptedStatusReader(Idle(100), Busy(100), noTape));

        Assert.False(outcome.IsSuccess);
        Assert.Contains("NO TAPE", outcome.ErrorMessage);
    }

    [Fact]
    public async Task Print_FailsWithoutSending_WhenThePrinterAlreadyReportsAProblem()
    {
        var coverOpen = new PrinterStatusSnapshot(1, [0x08], "READY", 100);

        var (outcome, stages) = await PrintAsync(new ScriptedStatusReader(coverOpen));

        Assert.False(outcome.IsSuccess);
        Assert.Contains("cover open", outcome.ErrorMessage);
        Assert.Empty(stages);
    }

    [Fact]
    public async Task Print_TimesOut_WhenThePrinterNeverPrints()
    {
        var (outcome, _) = await PrintAsync(new ScriptedStatusReader(Idle(100)));

        Assert.False(outcome.IsSuccess);
        Assert.Contains("9 mm tape", outcome.ErrorMessage);
    }

    [Fact]
    public async Task Print_SucceedsUnconfirmed_WhenThePrinterReportsNoStatus()
    {
        var (outcome, stages) = await PrintAsync(new ScriptedStatusReader([null]));

        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.Confirmed);
        Assert.Equal([PrintStage.Sending], stages);
    }

    [Fact]
    public async Task Print_FailsWithoutSending_WhenTheLoadedTapeDoesNotMatchTheLabel()
    {
        var twelveMmLoaded = new PrinterStatusSnapshot(3, [0x00], "READY", 100, "12mm(0.47\")");

        var (outcome, stages) = await PrintAsync(new ScriptedStatusReader(twelveMmLoaded)); // 9 mm label

        Assert.False(outcome.IsSuccess);
        Assert.Contains("12 mm tape loaded", outcome.ErrorMessage);
        Assert.Contains("needs 9 mm", outcome.ErrorMessage);
        Assert.Empty(stages);
    }

    [Fact]
    public async Task Print_Proceeds_WhenTheLoadedTapeMatchesTheLabel()
    {
        var nineMm = new PrinterStatusSnapshot(3, [0x00], "READY", 100, "9mm(0.35\")");

        var (outcome, _) = await PrintAsync(
            new ScriptedStatusReader(nineMm, nineMm with { LabelCount = 101 }));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
    }

    [Fact]
    public async Task Print_SendsQlPrintersAQlRasterJob()
    {
        var printer = PtPrinter();
        printer.Model = "Brother QL-820NWB";
        var model = BrotherCatalog.Find(printer.Model);
        using var label = new SKBitmap(4, 732);
        label.Erase(SKColors.White);

        var stages = new List<PrintStage>();
        var outcome = await Driver(new ScriptedStatusReader([null])).PrintAsync(
            printer, [label], model.Media.Single(media => media.Id == "dk-62"), PrinterCapabilities.Resolutions(printer.Model)[0],
            CutMode.AutoCut, stage => { stages.Add(stage); return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal([PrintStage.Sending], stages);
    }

    private static Printer CupsQueue() => new()
    {
        Name = "Via CUPS",
        Model = "Brother PT-P750W",
        ConnectionType = PrinterConnectionType.PrintServer,
        PrintServerAddress = "cups.example",
        Port = 631,
        QueueName = "Brother_PT-P750W"
    };

    [Fact]
    public async Task Print_ThroughAPrintServerQueue_SubmitsRawOverIpp_AndWaitsForTheJobToComplete()
    {
        var cups = new FakeIppServer(
            IppResponses.Printer(state: 3, acceptingJobs: true),
            IppResponses.Job(jobId: 42, state: 3),
            IppResponses.Job(jobId: 42, state: 5),
            IppResponses.Job(jobId: 42, state: 9));

        var (outcome, stages) = await PrintAsync(
            new ScriptedStatusReader([null]), printer: CupsQueue(), ipp: new IppClient(new HttpClient(cups)));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal([PrintStage.Sending, PrintStage.Printing], stages);
        Assert.Equal("http://cups.example:631/printers/Brother_PT-P750W", cups.Requests[1].Uri.ToString());
        Assert.True(FakeIppServer.Contains(cups.Requests[1].Body, "application/vnd.cups-raw"u8.ToArray()));
        Assert.True(FakeIppServer.Contains(cups.Requests[1].Body, [.. new byte[100], 0x1B, 0x40])); // the raster job
    }

    [Fact]
    public async Task Print_ThroughAPrintServerQueue_Fails_WhenTheServerAbortsTheJob()
    {
        var cups = new FakeIppServer(
            IppResponses.Printer(state: 3, acceptingJobs: true),
            IppResponses.Job(jobId: 7, state: 3),
            IppResponses.Job(jobId: 7, state: 8, message: "Backend failed"));

        var (outcome, _) = await PrintAsync(
            new ScriptedStatusReader([null]), printer: CupsQueue(), ipp: new IppClient(new HttpClient(cups)));

        Assert.False(outcome.IsSuccess);
        Assert.Contains("Backend failed", outcome.ErrorMessage);
    }

    [Fact]
    public async Task Print_ThroughAPrintServerQueue_CancelsTheJobAndReportsWhy_WhenItNeverFinishes()
    {
        const string reason = "Unable to locate printer \"BRW78465CA0DB3E.local\".";
        var cups = new FakeIppServer(operation => operation switch
        {
            FakeIppServer.GetPrinterAttributes => IppResponses.Printer(state: 4, acceptingJobs: true, message: reason),
            FakeIppServer.CancelJob => IppResponses.Error(0x0000, ""),
            _ => IppResponses.Job(jobId: 12, state: 5) // processing, forever
        });

        var (outcome, _) = await PrintAsync(
            new ScriptedStatusReader([null]), printer: CupsQueue(), ipp: new IppClient(new HttpClient(cups)));

        Assert.False(outcome.IsSuccess);
        Assert.Contains(reason, outcome.ErrorMessage);
        Assert.Contains(FakeIppServer.CancelJob, cups.Operations);
    }

    /// <summary>A CUPS server whose queue prints to the printer at socket://printer.test:9100.</summary>
    private static FakeIppServer CupsWithNetworkPrinter(params byte[][] jobStates)
    {
        var polls = 0;
        return new FakeIppServer(operation => operation switch
        {
            FakeIppServer.GetPrinterAttributes =>
                IppResponses.Printer(state: 3, acceptingJobs: true, deviceUri: "socket://printer.test:9100"),
            FakeIppServer.PrintJob => IppResponses.Job(jobId: 5, state: 3),
            FakeIppServer.CancelJob => IppResponses.Error(0x0000, ""),
            _ => jobStates[Math.Min(polls++, jobStates.Length - 1)]
        });
    }

    [Fact]
    public async Task Print_ThroughAPrintServerQueue_StopsBeforeSubmitting_WhenThePrinterHasTheWrongTape()
    {
        var cups = CupsWithNetworkPrinter(IppResponses.Job(jobId: 5, state: 9));
        var twelveMm = new PrinterStatusSnapshot(3, [0x00], "READY", 100, "12mm(0.47\")");

        var (outcome, stages) = await PrintAsync(
            new ScriptedStatusReader(twelveMm), printer: CupsQueue(), ipp: new IppClient(new HttpClient(cups)));

        Assert.False(outcome.IsSuccess);
        Assert.Contains("12 mm tape loaded", outcome.ErrorMessage);
        Assert.Empty(stages);
        Assert.DoesNotContain(FakeIppServer.PrintJob, cups.Operations);
    }

    [Fact]
    public async Task Print_ThroughAPrintServerQueue_IsConfirmedByThePrintersCounter()
    {
        var cups = CupsWithNetworkPrinter(IppResponses.Job(jobId: 5, state: 5), IppResponses.Job(jobId: 5, state: 9));
        var ready = new PrinterStatusSnapshot(3, [0x00], "READY", 100, "9mm(0.35\")");

        var (outcome, _) = await PrintAsync(
            new ScriptedStatusReader(ready, ready, ready with { LabelCount = 101 }),
            printer: CupsQueue(),
            ipp: new IppClient(new HttpClient(cups)));

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.True(outcome.Confirmed);
    }

    [Fact]
    public async Task Print_ThroughAPrintServerQueue_CancelsTheJob_WhenThePrinterStopsWhileCupsWorksOnIt()
    {
        var cups = CupsWithNetworkPrinter(IppResponses.Job(jobId: 5, state: 5)); // processing, forever
        var ready = new PrinterStatusSnapshot(3, [0x00], "READY", 100, "9mm(0.35\")");
        var coverOpen = new PrinterStatusSnapshot(1, [0x08], "COVER OPEN", 100, "9mm(0.35\")");

        var (outcome, _) = await PrintAsync(
            new ScriptedStatusReader(ready, coverOpen), printer: CupsQueue(), ipp: new IppClient(new HttpClient(cups)));

        Assert.False(outcome.IsSuccess);
        Assert.Contains("COVER OPEN", outcome.ErrorMessage);
        Assert.Contains(FakeIppServer.CancelJob, cups.Operations);
    }

    [Theory]
    [InlineData("socket://BRW78465CA0DB3E:9100", "brw78465ca0db3e")] // host names are case-insensitive
    [InlineData("socket://10.0.0.184:9100", "10.0.0.184")]
    [InlineData("ipp://printer.lan/ipp/print", "printer.lan")]
    [InlineData("lpd://10.0.0.184/queue", "10.0.0.184")]
    [InlineData("dnssd://Brother%20PT-P750W._ipp._tcp.local/?uuid=e3", null)]
    [InlineData("usb://Brother/PT-P750W?serial=H4G927809", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DeviceHost_FindsThePrintersNetworkHost_InACupsDeviceUri(string? deviceUri, string? host)
    {
        Assert.Equal(host, IppClient.DeviceHost(deviceUri));
    }

    [Fact]
    public async Task Print_ThroughAPrintServerQueue_DoesNotSubmit_WhenTheQueueIsNotAcceptingJobs()
    {
        var cups = new FakeIppServer(IppResponses.Printer(state: 5, acceptingJobs: false));

        var (outcome, stages) = await PrintAsync(
            new ScriptedStatusReader([null]), printer: CupsQueue(), ipp: new IppClient(new HttpClient(cups)));

        Assert.False(outcome.IsSuccess);
        Assert.Empty(stages);
        Assert.Single(cups.Requests);
    }

    [Theory]
    [InlineData("Brother PT-P750W", true)]
    [InlineData("pt p750w", true)]
    [InlineData("PT-E550W", true)]
    [InlineData("PT-P700", false)]
    [InlineData("PT-P910BT", false)]
    [InlineData(null, true)] // unknown: falls back to the PT-P750W
    public void Resolutions_OfferHighResolution_OnlyOnModelsThatSupportIt(string? model, bool high)
    {
        var qualities = PrinterCapabilities.Resolutions(model).Select(r => r.Quality).ToList();

        Assert.Equal(high ? [PrintQuality.Standard, PrintQuality.High] : [PrintQuality.Standard], qualities);
        Assert.Equal(PrintQuality.Standard, PrinterCapabilities.Resolve(model, high ? PrintQuality.Standard : PrintQuality.High).Quality);
    }
}
