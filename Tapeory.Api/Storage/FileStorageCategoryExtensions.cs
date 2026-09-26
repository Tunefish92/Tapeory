using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Storage;

public static class FileStorageCategoryExtensions
{
    public static string ToDirectoryName(this FileStorageCategory category) => category switch
    {
        FileStorageCategory.Image => "images",
        FileStorageCategory.OriginalLbx => "original-lbx",
        FileStorageCategory.PrintJobOutput => "print-jobs",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown storage category.")
    };
}
