using Tapeory.Api.Backups;
using Microsoft.AspNetCore.Mvc;

namespace Tapeory.Api.Controllers;

public sealed record RestoreBackupResponse(
    string RestoredFileName,
    BackupInfo SafetyBackup,
    int? RestoredTemplates,
    int? ReplacedTemplates);

/// <summary>
/// Database and label backups, stored in the storage folder. {kind} is "database" or "labels".
/// Only one create/restore/delete runs at a time; a second one gets 409.
/// </summary>
[ApiController]
[Route("api/backups/{kind}")]
public sealed class BackupsController(
    BackupStore store,
    DatabaseBackupService databaseBackups,
    LabelBackupService labelBackups,
    ILogger<BackupsController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult List(string kind) =>
        BackupFileNames.TryParseKind(kind, out var backupKind) ? Ok(store.List(backupKind)) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(string kind, CancellationToken cancellationToken)
    {
        if (!BackupFileNames.TryParseKind(kind, out var backupKind))
        {
            return NotFound();
        }

        using var operation = store.TryBeginOperation();

        if (operation is null)
        {
            return Busy();
        }

        try
        {
            var backup = await CreateBackupAsync(backupKind, beforeRestore: false, cancellationToken);
            return CreatedAtAction(nameof(Download), new { kind, fileName = backup.FileName }, backup);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Creating a {Kind} backup failed.", backupKind);
            return Problem($"Creating the backup failed: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{fileName}")]
    public IActionResult Download(string kind, string fileName)
    {
        if (!BackupFileNames.TryParseKind(kind, out var backupKind) || store.Find(backupKind, fileName) is not { } path)
        {
            return NotFound();
        }

        var contentType = backupKind == BackupKind.Database ? "application/sql" : "application/zip";
        return PhysicalFile(path, contentType, fileName);
    }

    /// <summary>Backs up the current state first (a "before restore" backup), then restores. If
    /// that safety backup fails, nothing is restored.</summary>
    [HttpPost("{fileName}/restore")]
    public async Task<IActionResult> Restore(string kind, string fileName, CancellationToken cancellationToken)
    {
        if (!BackupFileNames.TryParseKind(kind, out var backupKind))
        {
            return NotFound();
        }

        using var operation = store.TryBeginOperation();

        if (operation is null)
        {
            return Busy();
        }

        if (store.Find(backupKind, fileName) is not { } path)
        {
            return NotFound();
        }

        BackupInfo safetyBackup;

        try
        {
            safetyBackup = await CreateBackupAsync(backupKind, beforeRestore: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Backing up the current {Kind} before a restore failed.", backupKind);
            return Problem(
                $"Couldn't back up the current state first, so nothing was restored: {ex.Message}",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        try
        {
            if (backupKind == BackupKind.Database)
            {
                await databaseBackups.RestoreAsync(path);
                return Ok(new RestoreBackupResponse(fileName, safetyBackup, null, null));
            }

            var result = await labelBackups.RestoreAsync(path);
            return Ok(new RestoreBackupResponse(fileName, safetyBackup, result.RestoredTemplates, result.ReplacedTemplates));
        }
        catch (InvalidBackupException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restoring {Kind} backup {FileName} failed.", backupKind, fileName);
            return Problem(
                $"Restoring failed: {ex.Message} The state from before the restore is saved as {safetyBackup.FileName}.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpDelete("{fileName}")]
    public IActionResult Delete(string kind, string fileName)
    {
        if (!BackupFileNames.TryParseKind(kind, out var backupKind))
        {
            return NotFound();
        }

        using var operation = store.TryBeginOperation();

        if (operation is null)
        {
            return Busy();
        }

        return store.Delete(backupKind, fileName) ? NoContent() : NotFound();
    }

    private Task<BackupInfo> CreateBackupAsync(BackupKind kind, bool beforeRestore, CancellationToken cancellationToken) =>
        kind == BackupKind.Database
            ? databaseBackups.CreateAsync(beforeRestore, cancellationToken)
            : labelBackups.CreateAsync(beforeRestore, cancellationToken);

    private ObjectResult Busy() =>
        Problem("Another backup or restore is running. Try again when it has finished.", statusCode: StatusCodes.Status409Conflict);
}
