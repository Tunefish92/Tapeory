using System.Security.Claims;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Auth;

/// <summary>What a session cookie says about its account.</summary>
public static class AuthClaims
{
    public const string SecurityStamp = "tapeory:stamp";
    public const string DisplayName = "tapeory:display-name";
    public const string MustChangePassword = "tapeory:must-change-password";

    public static ClaimsPrincipal For(User user) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(DisplayName, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(SecurityStamp, user.SecurityStamp),
                new Claim(MustChangePassword, user.MustChangePassword ? "true" : "false"),
            ],
            AuthSetup.Scheme));

    public static int? UserId(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static bool MustChange(ClaimsPrincipal principal) =>
        principal.FindFirstValue(MustChangePassword) == "true";

    /// <summary>Whether the session still describes the account as it is now.</summary>
    public static bool Matches(ClaimsPrincipal principal, User user) =>
        principal.FindFirstValue(ClaimTypes.Name) == user.UserName
        && principal.FindFirstValue(DisplayName) == user.DisplayName
        && principal.IsInRole(user.Role.ToString())
        && principal.FindFirstValue(SecurityStamp) == user.SecurityStamp
        && MustChange(principal) == user.MustChangePassword;
}
