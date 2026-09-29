using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tapeory.Api.Printing;
using Tapeory.Api.Printing.Usb;
using Testcontainers.MySql;

namespace Tapeory.Api.Tests.Integration;

/// <summary>
/// Boots the real API host against a MySQL container (via Testcontainers), so integration
/// tests exercise EF Core / Pomelo against an actual server rather than a mocked one.
/// Requires a working Docker daemon on the machine running the tests.
/// </summary>
public sealed class TapeoryWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("tapeory_test")
        .WithUsername("tapeory")
        .WithPassword("tapeory")
        .Build();

    private readonly string _storagePath = Directory.CreateTempSubdirectory("tapeory-tests-").FullName;

    /// <summary>
    /// TAPEORY_TEST_DATABASE=sqlite runs every integration test against a local SQLite file
    /// instead of MySQL (and without Docker). Tests that only make sense on MySQL use
    /// <see cref="MySqlFactAttribute"/>.
    /// </summary>
    public static bool UseSqlite { get; } =
        string.Equals(Environment.GetEnvironmentVariable("TAPEORY_TEST_DATABASE"), "sqlite", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lets setup tests enter this container's details into an unconfigured host.</summary>
    public string MySqlHost => UseSqlite ? "localhost" : _mysql.Hostname;

    public int MySqlPort => UseSqlite ? MySqlBuilder.MySqlPort : _mysql.GetMappedPublicPort(MySqlBuilder.MySqlPort);

    public Task InitializeAsync() => UseSqlite ? Task.CompletedTask : _mysql.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _mysql.DisposeAsync();

        if (Directory.Exists(_storagePath))
        {
            Directory.Delete(_storagePath, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [UseSqlite ? "TAPEORY_DATABASE" : "ConnectionStrings:Default"] = UseSqlite ? "sqlite" : _mysql.GetConnectionString(),
                ["TAPEORY_STORAGE_PATH"] = _storagePath,
                ["TAPEORY_AUTO_MIGRATE"] = "true"
            });
        });

        // The fake printers in these tests are plain TCP listeners with no SNMP agent, and there's
        // no real CUPS server: tests script what the printer and the print server report instead.
        // By default the printer reports nothing, which skips the SNMP timeout on every print.
        // Short driver timeouts keep the "never finishes" cases quick.
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IPrinterStatusReader>(PrinterStatus);
            services.AddSingleton<IUsbPrinterPort>(UsbPort);
            services.AddSingleton(new IppClient(new HttpClient(PrintServer)));
            services.AddSingleton(provider => new BrotherPrinterDriver(
                provider.GetRequiredService<PrinterRawSocketSender>(),
                provider.GetRequiredService<IPrinterStatusReader>(),
                provider.GetRequiredService<IppClient>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BrotherPrinterDriver>>(),
                provider.GetRequiredService<IUsbPrinterPort>())
            {
                PollInterval = TimeSpan.FromMilliseconds(20),
                BaseTimeout = TimeSpan.FromSeconds(2),
                TimeoutPerLabel = TimeSpan.Zero
            });
        });
    }

    /// <summary>The (fake) USB printers on "this computer", and what was sent to them.</summary>
    public FakeUsbPort UsbPort { get; } = new();

    public sealed class FakeUsbPort : IUsbPrinterPort
    {
        public const string Device = "/dev/usb/lp0";

        public List<byte[]> Sent { get; } = [];

        public IReadOnlyList<UsbPrinterInfo> List() => [new(Device, "Brother PT-P750W", "PT-P750W")];

        public Task<RawSendResult> SendAsync(string identifier, byte[] data, CancellationToken cancellationToken)
        {
            if (identifier != Device)
            {
                return Task.FromResult(RawSendResult.Failure($"{identifier} isn't connected."));
            }

            lock (Sent)
            {
                Sent.Add(data);
            }

            return Task.FromResult(RawSendResult.Success());
        }
    }

    /// <summary>What the (fake) printer reports over SNMP. Reset it after a test that scripts it.</summary>
    public ScriptedPrinterStatus PrinterStatus { get; } = new();

    /// <summary>The (fake) CUPS server's answers. Reset it after a test that scripts it.</summary>
    public ScriptedPrintServer PrintServer { get; } = new();

    public sealed class ScriptedPrinterStatus : IPrinterStatusReader
    {
        private readonly Queue<PrinterStatusSnapshot?> _script = new();
        private PrinterStatusSnapshot? _last;

        /// <summary>Answers each read with the next snapshot, then keeps repeating the last one.</summary>
        public void Script(params PrinterStatusSnapshot?[] snapshots)
        {
            lock (_script)
            {
                _script.Clear();
                _last = null;
                foreach (var snapshot in snapshots) _script.Enqueue(snapshot);
            }
        }

        public void Reset() => Script();

        public Task<PrinterStatusSnapshot?> ReadAsync(string host, CancellationToken cancellationToken)
        {
            lock (_script)
            {
                if (_script.Count > 0) _last = _script.Dequeue();
                return Task.FromResult(_last);
            }
        }
    }

    public sealed class ScriptedPrintServer : HttpMessageHandler
    {
        private Func<int, byte[]>? _respond;

        public List<int> Operations { get; } = [];

        /// <summary>Answers each IPP request by its operation id.</summary>
        public void Script(Func<int, byte[]> respondToOperation)
        {
            Operations.Clear();
            _respond = respondToOperation;
        }

        public void Reset() => _respond = null;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            var operation = (body[2] << 8) | body[3];
            lock (Operations) Operations.Add(operation);

            return _respond is null
                ? new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(_respond(operation)) };
        }
    }
}
