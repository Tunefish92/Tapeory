using Tapeory.Api.Data;
using Tapeory.Api.Setup;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Tapeory.Api.Backups;

/// <summary>
/// Full SQL dumps of Tapeory's database (via MySqlBackup.NET, so no mysqldump binary is needed in
/// the container), stored in the storage folder, and restores from them. Files in the storage
/// folder (images, .lbx originals) aren't part of the dump; the label backup covers those.
///
/// Callers must hold <see cref="BackupStore.TryBeginOperation"/> for the duration of a call.
/// </summary>
public sealed class DatabaseBackupService(
    DatabaseConfigStore databaseConfig,
    BackupStore store,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DatabaseBackupService> logger)
{
    public Task<BackupInfo> CreateAsync(bool beforeRestore, CancellationToken cancellationToken) =>
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
        await Task.Run(() =>
        {
            using var connection = new MySqlConnection(BackupConnectionString());
            using var command = connection.CreateCommand();
            using var backup = new MySqlBackup(command);

            connection.Open();
            backup.ImportFromFile(path);
        });

        logger.LogInformation("Database restored from {Path}.", path);

        if (configuration.GetValue("TAPEORY_AUTO_MIGRATE", true))
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(CancellationToken.None);
        }
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
