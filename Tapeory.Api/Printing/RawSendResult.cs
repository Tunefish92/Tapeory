namespace Tapeory.Api.Printing;

public sealed record RawSendResult(bool IsSuccess, string? ErrorMessage)
{
    public static RawSendResult Success() => new(true, null);

    public static RawSendResult Failure(string errorMessage) => new(false, errorMessage);
}
