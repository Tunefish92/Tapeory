using Tapeory.Api;
using Tapeory.Api.Auth;
using Tapeory.Api.Backups;
using Tapeory.Api.Data;
using Tapeory.Api.Import;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Printers;
using Tapeory.Api.Printing;
using Tapeory.Api.Printing.Usb;
using Tapeory.Api.Rendering;
using Tapeory.Api.Settings;
using Tapeory.Api.Setup;
using Tapeory.Api.Stats;
using Tapeory.Api.Storage;
using Tapeory.Api.Templates;
using Tapeory.Api.Updates;
using Tapeory.Api.Uploads;
using Microsoft.EntityFrameworkCore;

using Tapeory.Api.Desktop;
using Tapeory.Api.Instances;

namespace Tapeory.Api;

/// <summary>How Tapeory is hosted: as the server (Docker, `dotnet run`) by default, or inside the
/// desktop app, which passes its own content folder and settings.</summary>
public sealed record TapeoryHostOptions
{
    /// <summary>The folder with wwwroot and Fonts; the working directory when null.</summary>
    public string? ContentRootPath { get; init; }

    /// <summary>Configuration values that win over appsettings and environment variables.</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; } = new Dictionary<string, string?>();
}

/// <summary>Builds the Tapeory web app: API, web UI, print queue, and the database migrations.</summary>
public static class TapeoryApp
{
    public static WebApplication Build(string[] args, TapeoryHostOptions? host = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = host?.ContentRootPath,
        });

        if (host is not null)
        {
            builder.Configuration.AddInMemoryCollection(host.Settings);
        }

        // The default loggers without the Windows event log: a normal user may not be allowed to write
        // to it, and a failed write stops the app (the desktop engine writes to its own log file).
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();
        builder.Logging.AddEventSourceLogger();

        // Named explicitly: when the desktop app hosts Tapeory, it's the entry assembly, and
        // controllers are only discovered there by default.
        builder.Services.AddControllers().AddApplicationPart(typeof(TapeoryApp).Assembly);
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<StorageService>();
        builder.Services.AddSingleton<TapeoryInstance>();
        builder.Services.AddSingleton<FileStorageService>();
        builder.Services.AddScoped<TemplateService>();
        builder.Services.AddScoped<ImageStore>();
        builder.Services.AddScoped<TemplateFileService>();
        builder.Services.AddScoped<LbxImportService>();
        builder.Services.AddSingleton<LabelRenderer>();
        builder.Services.AddScoped<UploadedFileImageResolver>();
        builder.Services.AddScoped<PrintJobService>();
        builder.Services.AddHostedService<PrintJobProcessor>();
        builder.Services.AddScoped<PrinterService>();
        builder.Services.AddScoped<StatsService>();
        builder.Services.AddScoped<AppSettingsService>();
        builder.Services.AddSingleton<FontCatalog>();
        builder.Services.AddSingleton<PrinterConnectionTester>();
        builder.Services.AddSingleton<PrinterRawSocketSender>();
        builder.Services.AddSingleton<IPrinterStatusReader, SnmpPrinterStatusReader>();
        builder.Services.AddSingleton(new IppClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }));
        builder.Services.AddSingleton(_ => IUsbPrinterPort.ForThisSystem());
        builder.Services.AddSingleton<BrotherPrinterDriver>();
        builder.Services.AddSingleton<DatabaseConfigStore>();
        builder.Services.AddSingleton<DatabaseSetupService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<BackupStore>();
        builder.Services.AddSingleton<DatabaseBackupService>();
        builder.Services.AddScoped<LabelBackupService>();
        builder.Services.AddTapeoryAuth();
        builder.Services.AddSingleton(services => new UpdateChecker(
            new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
            services.GetRequiredService<TimeProvider>(),
            // Another "latest release" address, to try a release before publishing it.
            Environment.GetEnvironmentVariable("TAPEORY_UPDATE_FEED")));

        // The database is chosen per scope rather than once at startup: on a fresh install there is none
        // until the first-run setup saves one, and the app switches over without a restart.
        builder.Services.AddScoped<AppDbContext>(services => DatabaseContexts.Create(
            services.GetRequiredService<DatabaseConfigStore>(),
            services.GetRequiredService<ILoggerFactory>()));

        var app = builder.Build();

        app.UseExceptionHandler();

        // In the desktop app, only its own window gets in (see DesktopGuard).
        app.UseDesktopGuard();

        // Serves the built React app (copied into wwwroot by the production Docker image) so the API
        // and web UI ship as a single container. In local development wwwroot is empty and these two
        // calls are inert — the frontend is served separately by the Vite dev server instead.
        // The desktop app has its own interface: no web UI there.
        var desktop = DesktopGuard.IsDesktop(app.Configuration);
        if (!desktop)
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }

        app.UseAuthentication();
        app.UseCsrfCheck();
        app.UseAuthorization();

        // Until the database connection has been entered, only setup and health work; every other API
        // call answers 503 so the web UI knows to show the setup screen.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var needsDatabase = path.StartsWithSegments("/api")
                && !path.StartsWithSegments("/api/setup")
                && !path.StartsWithSegments("/api/health");

            if (needsDatabase && !context.RequestServices.GetRequiredService<DatabaseConfigStore>().IsConfigured)
            {
                await Results.Problem(
                    title: "The database connection has not been set up yet.",
                    detail: "Open Tapeory in a browser to enter the database connection details.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?> { ["setupRequired"] = true })
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });

        app.MapControllers();

        app.MapGet("/api/health", async (StorageService storage, DatabaseConfigStore database, AppDbContext db) =>
        {
            var canConnect = database.IsConfigured && await db.Database.CanConnectAsync();

            return Results.Ok(new
            {
                status = "ok",
                storagePath = storage.RootPath,
                databaseConfigured = database.IsConfigured,
                databaseConnected = canConnect
            });
        }).AllowAnonymous();

        // Client-side routing fallback: any GET that isn't an API route or a real static file falls
        // through to index.html so React Router can handle it.
        if (!desktop)
        {
            app.MapFallbackToFile("index.html").AllowAnonymous();
        }

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            app.Services.GetRequiredService<StorageService>().EnsureDirectories();
        });

        if (!app.Services.GetRequiredService<DatabaseConfigStore>().IsConfigured)
        {
            app.Logger.LogWarning(
                "No database connection configured yet. Open the web UI to enter it; migrations run once it's saved.");
        }
        else if (app.Configuration.GetValue("TAPEORY_AUTO_MIGRATE", true))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();

            if (db is SqliteAppDbContext)
            {
                // Readers don't wait for the print queue's writes, and the other way round.
                db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            }
        }

        return app;
    }
}
