using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;

namespace Tapeory.Api.Tests.Unit;

public sealed class BrotherPtPrinterDriverTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly SKBitmap _label = new(4, 64);

    public BrotherPtPrinterDriverTests()
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

    private static BrotherPtPrinterDriver Driver(IPrinterStatusReader status, IppClient? ipp = null) =>
        new(new PrinterRawSocketSender(), status, ipp ?? new IppClient(new HttpClient()), NullLogger<BrotherPtPrinterDriver>.Instance)
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
            BrotherPtTape.ForLabelHeight(9m),
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
    public async Task Print_RefusesQlPrinters()
    {
        var printer = PtPrinter();
        printer.Model = "Brother QL-820NWB";

        var (outcome, stages) = await PrintAsync(new ScriptedStatusReader(Idle(0)), printer: printer);

        Assert.False(outcome.IsSuccess);
        Assert.Empty(stages);
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
    [InlineData("Brother PT-H110", false)]
    [InlineData(null, false)]
    public void Resolutions_OfferHighResolution_OnlyOnModelsThatSupportIt(string? model, bool high)
    {
        var qualities = PrinterCapabilities.Resolutions(model).Select(r => r.Quality).ToList();

        Assert.Equal(high ? [PrintQuality.Standard, PrintQuality.High] : [PrintQuality.Standard], qualities);
        Assert.Equal(PrintQuality.Standard, PrinterCapabilities.Resolve(model, high ? PrintQuality.Standard : PrintQuality.High).Quality);
    }
}
