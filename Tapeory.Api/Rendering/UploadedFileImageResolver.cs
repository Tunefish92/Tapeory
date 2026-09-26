using Tapeory.Api.Data;
using Tapeory.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.Rendering;

/// <summary>Adapts UploadedFile lookups to the renderer's ImageResolver delegate shape, shared by
/// the on-demand preview endpoint and the background print-job processor.</summary>
public sealed class UploadedFileImageResolver(AppDbContext db, FileStorageService fileStorage)
{
    public byte[]? Resolve(int uploadedFileId)
    {
        var file = db.UploadedFiles.AsNoTracking().FirstOrDefault(f => f.Id == uploadedFileId);

        if (file is null)
        {
            return null;
        }

        using var stream = fileStorage.OpenRead(file.RelativePath);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public ImageResolver AsDelegate() => Resolve;
}
