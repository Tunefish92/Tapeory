namespace Tapeory.Api.Auth;

public static class AccountErrors
{
    // A wrong password is a 400, not a 401: the web UI treats 401 as "your session ended".
    public static int StatusCode(AccountError error) => error switch
    {
        AccountError.Conflict => StatusCodes.Status409Conflict,
        AccountError.NotFound => StatusCodes.Status404NotFound,
        AccountError.Throttled => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status400BadRequest
    };
}
