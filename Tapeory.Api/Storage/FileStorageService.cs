using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Storage;

public sealed class FileStorageService(StorageService storage)
{
    public async Task<StoredFile> SaveAsync(
        Stream content,
        string originalFileName,
        FileStorageCategory category,
        CancellationToken cancellationToken = default)
    {
        var directoryName = category.ToDirectoryName();
        var fileName = SafeFileNaming.GenerateStoredFileName(originalFileName);
        var relativePath = Path.Combine(directoryName, fileName).Replace('\\', '/');
        var fullPath = StoragePathGuard.ResolveWithinRoot(storage.RootPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var destination = File.Create(fullPath))
        {
            await content.CopyToAsync(destination, cancellationToken);
        }

        var sizeBytes = new FileInfo(fullPath).Length;

        return new StoredFile(fileName, relativePath, sizeBytes);
    }

    public Stream OpenRead(string relativePath)
    {
        var fullPath = StoragePathGuard.ResolveWithinRoot(storage.RootPath, relativePath);
        return File.OpenRead(fullPath);
    }

    public void Delete(string relativePath)
    {
        var fullPath = StoragePathGuard.ResolveWithinRoot(storage.RootPath, relativePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
