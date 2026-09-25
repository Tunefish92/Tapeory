namespace Tapeory.Api.Uploads;

public sealed record UploadedImageResponse(
    int Id,
    string FileName,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Url,
    DateTimeOffset CreatedAt);
