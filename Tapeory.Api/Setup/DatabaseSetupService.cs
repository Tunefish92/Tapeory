using Tapeory.Api.Data;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Tapeory.Api.Setup;

/// <summary>Partial on purpose: missing values are reported as validation errors, not a
/// generic model-binding failure.</summary>
public sealed record DatabaseSetupRequest(string? Host, int? Port, string? Database, string? User, string? Password)
{
    public DatabaseConnectionSettings ToSettings() => new(
        Host?.Trim() ?? "",
        Port ?? DatabaseConnectionSettings.DefaultPort,
        Database?.Trim() ?? "",
        User?.Trim() ?? "",
        Password ?? "");
}

public sealed record SetupStatusResponse(bool Configured);

/// <param name="DatabaseExists">False when the server is reachable but the database doesn't
/// exist yet — setup then creates it (if the user is allowed to).</param>
public sealed record DatabaseConnectionTestResult(bool IsSuccess, string? ErrorMessage, bool DatabaseExists)
{
    public static DatabaseConnectionTestResult Failure(string errorMessage) => new(false, errorMessage, false);
}

public enum DatabaseSetupOutcome
{
    Configured,
    AlreadyConfigured,
    Failed
}

public sealed record DatabaseSetupResult(DatabaseSetupOutcome Outcome, string? ErrorMessage = null);

/// <summary>Tests and applies the database connection entered in the first-run setup.</summary>
public sealed class DatabaseSetupService(
    DatabaseConfigStore store,
    IConfiguration configuration,
    ILogger<DatabaseSetupService> logger)
{
    public static readonly ServerVersion ServerVersion = new MySqlServerVersion(new Version(8, 0, 0));

    private const uint TestConnectTimeoutSeconds = 10;

    private readonly SemaphoreSlim _configureGate = new(1, 1);

    public async Task<DatabaseConnectionTestResult> TestAsync(
        DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            await OpenAsync(settings.ToConnectionString(connectTimeoutSeconds: TestConnectTimeoutSeconds), cancellationToken);
            return new DatabaseConnectionTestResult(true, null, DatabaseExists: true);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.UnknownDatabase)
        {
            // The credentials work but the database isn't there yet. Check the server connection
            // on its own; migrations will create the database during setup.
            try
            {
                await OpenAsync(
                    settings.ToConnectionString(includeDatabase: false, connectTimeoutSeconds: TestConnectTimeoutSeconds),
                    cancellationToken);
                return new DatabaseConnectionTestResult(true, null, DatabaseExists: false);
            }
            catch (Exception inner) when (inner is not OperationCanceledException)
            {
                return Fail(settings, inner);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(settings, ex);
        }
    }

    public async Task<DatabaseSetupResult> ConfigureAsync(
        DatabaseConnectionSettings settings, CancellationToken cancellationToken)
    {
        await _configureGate.WaitAsync(cancellationToken);

        try
        {
            if (store.IsConfigured)
            {
                return new DatabaseSetupResult(DatabaseSetupOutcome.AlreadyConfigured);
            }

            var test = await TestAsync(settings, cancellationToken);

            if (!test.IsSuccess)
            {
                return new DatabaseSetupResult(DatabaseSetupOutcome.Failed, test.ErrorMessage);
            }

            // Migrate before saving: if the schema can't be created (e.g. missing privileges),
            // nothing is stored and the user can simply correct the details and try again.
            if (configuration.GetValue("TAPEORY_AUTO_MIGRATE", true))
            {
                try
                {
                    var options = new DbContextOptionsBuilder<AppDbContext>()
                        .UseMySql(settings.ToConnectionString(), ServerVersion)
                        .Options;

                    await using var db = new AppDbContext(options);
                    // Not cancellable: a half-applied migration is worse than a slow request.
                    await db.Database.MigrateAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Applying database migrations during setup failed.");
                    return new DatabaseSetupResult(
                        DatabaseSetupOutcome.Failed,
                        $"Connected, but creating Tapeory's tables failed: {ex.Message}");
                }
            }

            store.Save(settings);
            return new DatabaseSetupResult(DatabaseSetupOutcome.Configured);
        }
        finally
        {
            _configureGate.Release();
        }
    }

    private static async Task OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
    }

    private DatabaseConnectionTestResult Fail(DatabaseConnectionSettings settings, Exception ex)
    {
        logger.LogInformation(
            "Database connection test to {Host}:{Port} failed: {Message}", settings.Host, settings.Port, ex.Message);
        return DatabaseConnectionTestResult.Failure(ex.Message);
    }
}
