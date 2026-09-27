namespace Tapeory.Api.Data.Entities;

/// <summary>
/// An account. Until the first one exists, Tapeory is open to anyone who can reach it; after that,
/// every API call needs a signed-in account.
/// </summary>
public sealed class User
{
    public int Id { get; set; }

    /// <summary>What the account signs in with. Unique ignoring case (the column's collation).</summary>
    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>A disabled account can't sign in, and its sessions end.</summary>
    public bool Disabled { get; set; }

    /// <summary>Set by a temporary password (a new account or a reset): the account has to choose
    /// its own password before it can do anything else.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Changes with every password change, reset or disabling. Sessions carry the stamp
    /// they were signed in with, so a changed stamp ends them.</summary>
    public string SecurityStamp { get; set; } = NewSecurityStamp();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
}
