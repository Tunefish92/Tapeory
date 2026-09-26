namespace Tapeory.Api;

public sealed class StorageService
{
    private static readonly string[] RequiredDirectories =
    [
        "templates",
        "uploads",
        "images",
        "exports",
        "backups",
        "original-lbx",
        "print-jobs"
    ];

    public StorageService(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredPath = configuration["TAPEORY_STORAGE_PATH"];
        // Not "data" — on a case-insensitive filesystem (Windows, default macOS) that collides
        // with the Data/ source folder next to this project, since ContentRootPath for local
        // `dotnet run` is Tapeory.Api/ itself. Docker always sets TAPEORY_STORAGE_PATH=/data
        // explicitly, so this default only matters for local runs outside a container.
        RootPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "local-storage")
            : Path.GetFullPath(configuredPath);
    }

    public string RootPath { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootPath);

        foreach (var directory in RequiredDirectories)
        {
            Directory.CreateDirectory(Path.Combine(RootPath, directory));
        }
    }
}