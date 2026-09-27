using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Auth;

public static partial class AccountRules
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 200;

    // No 0/O or 1/l/I, so a temporary password can be read out or typed without mix-ups.
    private const string TemporaryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    [GeneratedRegex(@"^[\p{L}\p{N}._@-]{3,50}$")]
    private static partial Regex UserNamePattern();

    public static string? UserNameError(string? userName) =>
        userName is not null && UserNamePattern().IsMatch(userName.Trim())
            ? null
            : "User names are 3–50 letters, digits, dots, dashes, underscores or @.";

    public static string? DisplayNameError(string? displayName) =>
        displayName is not null && displayName.Trim().Length > 100 ? "Display names are at most 100 characters." : null;

    public static string? PasswordError(string? password) =>
        password is null || password.Length < MinPasswordLength
            ? $"Passwords need at least {MinPasswordLength} characters."
            : password.Length > MaxPasswordLength
                ? $"Passwords are at most {MaxPasswordLength} characters."
                : null;

    public static bool TryParseRole(string? value, out UserRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);

    /// <summary>The display name, falling back to the user name when left empty.</summary>
    public static string DisplayNameOrUserName(string? displayName, string userName) =>
        string.IsNullOrWhiteSpace(displayName) ? userName : displayName.Trim();

    public static string NewTemporaryPassword() =>
        RandomNumberGenerator.GetString(TemporaryAlphabet, 12);
}
