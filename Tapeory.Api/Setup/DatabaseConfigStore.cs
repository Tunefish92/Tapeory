using System.Text.Json;

namespace Tapeory.Api.Setup;

/// <summary>
/// Owns the database connection string. On a fresh install there is none: the web UI shows the
/// first-run setup, and what the user enters there is saved to <c>config/database.json</c> inside
/// the storage folder (so it lives on the persistent <c>/data</c> volume in Docker) and used from
/// then on — no restart needed, since the DbContext reads <see cref="ConnectionString"/> per scope.
///
/// An explicitly configured <c>ConnectionStrings:Default</c> (e.g. the
/// <c>ConnectionStrings__Default</c> environment variable) still wins over the file and skips the
/// setup entirely, for deployments that prefer to pass it in from outside.
/// </summary>
public sealed class DatabaseConfigStore
{
    public const string ConfigDirectoryName = "config";
    public const string FileName = "database.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string? _overrideConnectionString;
    private readonly ILogger<DatabaseConfigStore> _logger;
    private readonly Lock _saveLock = new();
    private volatile string? _savedConnectionString;

    public DatabaseConfigStore(IConfiguration configuration, StorageService storage, ILogger<DatabaseConfigStore> logger)
    {
        _logger = logger;
        FilePath = Path.Combine(storage.RootPath, ConfigDirectoryName, FileName);

        var configured = configuration.GetConnectionString("Default");
        _overrideConnectionString = string.IsNullOrWhiteSpace(configured) ? null : configured;
        _savedConnectionString = Load()?.ToConnectionString();
    }

    /// <summary>Where the connection details entered during setup are stored.</summary>
    public string FilePath { get; }

    /// <summary>Null until the database has been set up.</summary>
    public string? ConnectionString => _overrideConnectionString ?? _savedConnectionString;

    public bool IsConfigured => ConnectionString is not null;

    /// <summary>Persists the settings and switches the app over to them immediately.</summary>
    public void Save(DatabaseConnectionSettings settings)
    {
        lock (_saveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Write-then-rename, so a crash mid-write can't leave a truncated file behind that
            // would send the next start back into setup.
            var tempPath = FilePath + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };

            if (!OperatingSystem.IsWindows())
            {
                // The file holds the database password in plain text: owner read/write only.
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(tempPath, options))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
            }

            File.Move(tempPath, FilePath, overwrite: true);
            _savedConnectionString = settings.ToConnectionString();
        }

        _logger.LogInformation("Database connection settings saved to {Path}.", FilePath);
    }

    private DatabaseConnectionSettings? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<DatabaseConnectionSettings>(File.ReadAllText(FilePath), JsonOptions);

            if (settings is not null && settings.Validate().Count == 0)
            {
                return settings;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse {Path}.", FilePath);
        }

        _logger.LogWarning(
            "The database settings in {Path} are invalid and were ignored; the web UI will ask for them again.", FilePath);
        return null;
    }
}
