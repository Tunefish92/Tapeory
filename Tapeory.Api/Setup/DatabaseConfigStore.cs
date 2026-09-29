using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Tapeory.Api.Setup;

public enum DatabaseProvider
{
    MySql,
    /// <summary>A local database file in the storage folder, for the desktop app.</summary>
    Sqlite
}

/// <summary>
/// Owns the database connection string. On a fresh install there is none: the web UI shows the
/// first-run setup, and what the user enters there is saved to <c>config/database.json</c> inside
/// the storage folder (so it lives on the persistent <c>/data</c> volume in Docker) and used from
/// then on — no restart needed, since the DbContext reads <see cref="ConnectionString"/> per scope.
///
/// An explicitly configured <c>ConnectionStrings:Default</c> (e.g. the
/// <c>ConnectionStrings__Default</c> environment variable) still wins over the file and skips the
/// setup entirely, for deployments that prefer to pass it in from outside. <c>TAPEORY_DATABASE=sqlite</c>
/// likewise skips it and uses a local SQLite file (<see cref="SqlitePath"/>).
/// </summary>
public sealed class DatabaseConfigStore
{
    public const string ConfigDirectoryName = "config";
    public const string FileName = "database.json";
    public const string SqliteFileName = "tapeory.db";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Connection? _override;
    private readonly ILogger<DatabaseConfigStore> _logger;
    private readonly Lock _saveLock = new();
    private volatile Connection? _saved;

    public DatabaseConfigStore(IConfiguration configuration, StorageService storage, ILogger<DatabaseConfigStore> logger)
    {
        _logger = logger;
        FilePath = Path.Combine(storage.RootPath, ConfigDirectoryName, FileName);
        SqlitePath = Path.Combine(storage.RootPath, SqliteFileName);

        var configured = configuration.GetConnectionString("Default");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            _override = new Connection(DatabaseProvider.MySql, configured);
        }
        else if (string.Equals(configuration["TAPEORY_DATABASE"], "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            _override = SqliteConnection();
        }

        _saved = Load();
    }

    /// <summary>Where the connection details entered during setup are stored.</summary>
    public string FilePath { get; }

    /// <summary>The local database file, when <see cref="Provider"/> is SQLite.</summary>
    public string SqlitePath { get; }

    /// <summary>Null until the database has been set up.</summary>
    public string? ConnectionString => (_override ?? _saved)?.ConnectionString;

    /// <summary>Which database the connection string is for.</summary>
    public DatabaseProvider Provider => (_override ?? _saved)?.Provider ?? DatabaseProvider.MySql;

    public bool IsConfigured => ConnectionString is not null;

    /// <summary>Switches to a local SQLite database in the storage folder, and remembers it.</summary>
    public void SaveSqlite()
    {
        lock (_saveLock)
        {
            Write(stream => JsonSerializer.Serialize(stream, new { provider = "sqlite" }, JsonOptions));
            _saved = SqliteConnection();
        }

        _logger.LogInformation("Using the local SQLite database {Path}.", SqlitePath);
    }

    /// <summary>Persists the settings and switches the app over to them immediately.</summary>
    public void Save(DatabaseConnectionSettings settings)
    {
        lock (_saveLock)
        {
            Write(stream => JsonSerializer.Serialize(stream, settings, JsonOptions));
            _saved = new Connection(DatabaseProvider.MySql, settings.ToConnectionString());
        }

        _logger.LogInformation("Database connection settings saved to {Path}.", FilePath);
    }

    private Connection SqliteConnection() =>
        new(DatabaseProvider.Sqlite, new SqliteConnectionStringBuilder
        {
            DataSource = SqlitePath,
            // Waits this long for another connection's write to finish instead of failing.
            DefaultTimeout = 30
        }.ToString());

    private void Write(Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        // Write-then-rename, so a crash mid-write can't leave a truncated file behind that would
        // send the next start back into setup.
        var tempPath = FilePath + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };

        if (!OperatingSystem.IsWindows())
        {
            // The file can hold the database password in plain text: owner read/write only.
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(tempPath, options))
        {
            write(stream);
        }

        File.Move(tempPath, FilePath, overwrite: true);
    }

    private Connection? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(FilePath);

            using (var document = JsonDocument.Parse(text))
            {
                if (document.RootElement.TryGetProperty("provider", out var provider)
                    && string.Equals(provider.GetString(), "sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    return SqliteConnection();
                }
            }

            var settings = JsonSerializer.Deserialize<DatabaseConnectionSettings>(text, JsonOptions);

            if (settings is not null && settings.Validate().Count == 0)
            {
                return new Connection(DatabaseProvider.MySql, settings.ToConnectionString());
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

    private sealed record Connection(DatabaseProvider Provider, string ConnectionString);
}
