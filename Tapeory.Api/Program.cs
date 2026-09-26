using Tapeory.Api;
using Tapeory.Api.Backups;
using Tapeory.Api.Data;
using Tapeory.Api.Import;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Printers;
using Tapeory.Api.Printing;
using Tapeory.Api.Rendering;
using Tapeory.Api.Settings;
using Tapeory.Api.Setup;
using Tapeory.Api.Stats;
using Tapeory.Api.Storage;
using Tapeory.Api.Templates;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddScoped<TemplateService>();
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
builder.Services.AddSingleton<DatabaseConfigStore>();
builder.Services.AddSingleton<DatabaseSetupService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BackupStore>();
builder.Services.AddSingleton<DatabaseBackupService>();
builder.Services.AddScoped<LabelBackupService>();

// The connection string is read per scope rather than once at startup: on a fresh install there
// is none until the first-run setup saves one, and the app switches over without a restart.
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<DatabaseConfigStore>().ConnectionString;

    if (connectionString is null)
    {
        // Lets the context be constructed (and `dotnet ef migrations add` run); any actual
        // query would fail, which the setup-required middleware below prevents.
        options.UseMySql(DatabaseSetupService.ServerVersion);
    }
    else
    {
        options.UseMySql(connectionString, DatabaseSetupService.ServerVersion);
    }
});

var app = builder.Build();

app.UseExceptionHandler();

// Serves the built React app (copied into wwwroot by the production Docker image) so the API
// and web UI ship as a single container. In local development wwwroot is empty and these two
// calls are inert — the frontend is served separately by the Vite dev server instead.
app.UseDefaultFiles();
app.UseStaticFiles();

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
});

// Client-side routing fallback: any GET that isn't an API route or a real static file falls
// through to index.html so React Router can handle it.
app.MapFallbackToFile("index.html");

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
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.Run();

// Exposed so WebApplicationFactory<Program> can bootstrap the app in integration tests.
public partial class Program;
