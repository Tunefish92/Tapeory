namespace Tapeory.Api.Printing;

public sealed record ConnectionTestResult(bool IsSuccess, string? ErrorMessage)
{
    public static ConnectionTestResult Success() => new(true, null);

    public static ConnectionTestResult Failure(string errorMessage) => new(false, errorMessage);
}
