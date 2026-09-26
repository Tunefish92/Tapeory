namespace Tapeory.Api.Storage;

public sealed record StoredFile(string FileName, string RelativePath, long SizeBytes);
