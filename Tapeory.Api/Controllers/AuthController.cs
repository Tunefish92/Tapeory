using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tapeory.Api.Auth;
using Tapeory.Api.Data;

namespace Tapeory.Api.Controllers;

/// <summary>Signing in and out, the first account, and changing your own password.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(AccountService accounts, UserDirectory directory, AppDbContext db, IConfiguration configuration) : ControllerBase
{
    /// <summary>Whether accounts exist yet, and who is signed in: tells the web UI to show the app,
    /// the sign-in page, or "create your account".</summary>
    [HttpGet("state")]
    [AllowAnonymous]
    public async Task<IActionResult> GetState(CancellationToken cancellationToken)
    {
        var hasUsers = await directory.HasUsersAsync(cancellationToken);
        var user = AuthClaims.UserId(User) is { } id ? await db.Users.FindAsync([id], cancellationToken) : null;

        return Ok(new AuthStateResponse(
            hasUsers,
            user is null ? null : CurrentUserResponse.From(user),
            Desktop.DesktopGuard.IsDesktop(configuration)));
    }

    /// <summary>Creates the first account, which is the administrator. Refused once any exists.</summary>
    [HttpPost("first-user")]
    [AllowAnonymous]
    public async Task<IActionResult> CreateFirstUser(FirstUserRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.CreateFirstUserAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            return Failure(result);
        }

        await HttpContext.SignInAsync(AuthSetup.Scheme, AuthClaims.For(result.Value!), new AuthenticationProperties { IsPersistent = true });
        return Ok(new AuthStateResponse(true, CurrentUserResponse.From(result.Value!)));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.SignInAsync(request, Address, cancellationToken);

        if (!result.Succeeded)
        {
            return Failure(result);
        }

        await HttpContext.SignInAsync(
            AuthSetup.Scheme,
            AuthClaims.For(result.Value!),
            new AuthenticationProperties { IsPersistent = request.RememberMe });
        return Ok(new AuthStateResponse(true, CurrentUserResponse.From(result.Value!)));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSetup.Scheme);
        return NoContent();
    }

    /// <summary>Changes the signed-in account's password. Its other sessions end; this one stays.</summary>
    [HttpPut("password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.ChangePasswordAsync(AuthClaims.UserId(User)!.Value, request, Address, cancellationToken);

        if (!result.Succeeded)
        {
            return Failure(result);
        }

        // The new security stamp ends every session, so this one signs in again with it.
        var remembered = (await HttpContext.AuthenticateAsync(AuthSetup.Scheme)).Properties?.IsPersistent ?? false;
        await HttpContext.SignInAsync(
            AuthSetup.Scheme,
            AuthClaims.For(result.Value!),
            new AuthenticationProperties { IsPersistent = remembered });
        return Ok(new AuthStateResponse(true, CurrentUserResponse.From(result.Value!)));
    }

    private string? Address => HttpContext.Connection.RemoteIpAddress?.ToString();

    private ObjectResult Failure<T>(AccountResult<T> result)
    {
        if (result.RetryAfter is { } wait)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
        }

        return Problem(title: result.Message, statusCode: AccountErrors.StatusCode(result.Error));
    }
}
