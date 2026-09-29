using Tapeory.Api.Auth;
using Tapeory.Api.Data;
using Tapeory.Api.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace Tapeory.Api.Backups;

/// <summary>
/// Full SQL dumps of Tapeory's database (via MySqlBackup.NET, so no mysqldump binary is needed in
/// the container), or copies of a local SQLite database, stored in the storage folder, and
/// restores from them. Files in the storage
/// folder (images, .lbx originals) aren't part of the dump; the label backup covers those.
///
/// Callers must hold <see cref="BackupStore.TryBeginOperation"/> for the duration of a call.
/// </summary>
public sealed class DatabaseBackupService(
    DatabaseConfigStore databaseConfig,
    BackupStore store,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    UserDirectory users,
    ILogger<DatabaseBackupService> logger)
{
    public Task<BackupInfo> CreateAsync(bool beforeRestore, CancellationToken cancellationToken) =>
        databaseConfig.Provider == DatabaseProvider.Sqlite
            ? store.WriteAsync(
                BackupKind.Database,
                beforeRestore,
                path => Task.Run(() => CopySqlite(SqliteConnectionString(), $"Data Source={path};Pooling=False"), cancellationToken),
                BackupFileNames.SqliteExtension)
            : CreateMySqlAsync(beforeRestore, cancellationToken);

    private Task<BackupInfo> CreateMySqlAsync(bool beforeRestore, CancellationToken cancellationToken) =>
        store.WriteAsync(BackupKind.Database, beforeRestore, path => Task.Run(() =>
        {
            using var connection = new MySqlConnection(BackupConnectionString());
            using var command = connection.CreateCommand();
            using var backup = new MySqlBackup(command);

            connection.Open();

            // Tables and rows only: Tapeory creates no routines, views, triggers or events, and
            // reading them needs privileges the app's database user may not have.
            backup.ExportInfo.AddDropTable = true;
            backup.ExportInfo.ExportProcedures = false;
            backup.ExportInfo.ExportFunctions = false;
            backup.ExportInfo.ExportViews = false;
            backup.ExportInfo.ExportTriggers = false;
            backup.ExportInfo.ExportEvents = false;

            backup.ExportToFile(path);
        }, cancellationToken));

    /// <summary>
    /// Replaces the whole database with the dump at <paramref name="path"/>, then applies any
    /// migrations the dump predates, so backups from older Tapeory versions still work. Not
    /// cancellable: stopping halfway would leave a partly restored database.
    /// </summary>
    public async Task RestoreAsync(string path)
    {
        var sqlite = databaseConfig.Provider == DatabaseProvider.Sqlite;

        if (sqlite != BackupFileNames.IsSqliteBackup(Path.GetFileName(path)))
        {
            throw new InvalidBackupException(sqlite
                ? "This backup is from a MySQL database; Tapeory uses a local database here, so it can't be restored. Label backups work on both."
                : "This backup is from a local (SQLite) database, so it can't be restored into MySQL. Label backups work on both.");
        }

        await Task.Run(() =>
        {
            if (sqlite)
            {
                // The online backup API copies the backup over the live database page by page.
                CopySqlite($"Data Source={path};Mode=ReadOnly;Pooling=False", SqliteConnectionString());
                return;
            }

            using var connection = new MySqlConnection(BackupConnectionString());
            using var command = connection.CreateCommand();
            using var backup = new MySqlBackup(command);

            connection.Open();
            backup.ImportFromFile(path);
        });

        logger.LogInformation("Database restored from {Path}.", path);

        // The restored database brings its own accounts, or none if it predates them.
        users.Reset();

        if (configuration.GetValue("TAPEORY_AUTO_MIGRATE", true))
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(CancellationToken.None);
        }
    }

    private string SqliteConnectionString() =>
        databaseConfig.ConnectionString ?? throw new InvalidOperationException("The database has not been set up yet.");

    private static void CopySqlite(string sourceConnectionString, string targetConnectionString)
    {
        using var source = new SqliteConnection(sourceConnectionString);
        using var target = new SqliteConnection(targetConnectionString);
        source.Open();
        target.Open();
        source.BackupDatabase(target);
    }

    private string BackupConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder(
            databaseConfig.ConnectionString
            ?? throw new InvalidOperationException("The database connection has not been set up yet."))
        {
            // Dumps use session variables (SET @OLD_…), which MySqlConnector otherwise reads as
            // missing command parameters.
            AllowUserVariables = true,
            ConvertZeroDateTime = true,
            // A single statement of a large restore can take longer than the 30-second default.
            DefaultCommandTimeout = 600
        };

        return builder.ConnectionString;
    }
}
