using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Auth;

public sealed record AuthStateResponse(bool HasUsers, CurrentUserResponse? User);

public sealed record CurrentUserResponse(
    int Id,
    string UserName,
    string DisplayName,
    string Role,
    bool MustChangePassword)
{
    public static CurrentUserResponse From(User user) =>
        new(user.Id, user.UserName, user.DisplayName, user.Role.ToString(), user.MustChangePassword);
}

public sealed record UserResponse(
    int Id,
    string UserName,
    string DisplayName,
    string Role,
    bool Disabled,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt)
{
    public static UserResponse From(User user) =>
        new(user.Id, user.UserName, user.DisplayName, user.Role.ToString(), user.Disabled,
            user.MustChangePassword, user.CreatedAt, user.LastLoginAt);
}

/// <summary>A new account, or one with a reset password: the temporary password is shown once.</summary>
public sealed record TemporaryPasswordResponse(UserResponse User, string TemporaryPassword);

public sealed record FirstUserRequest(string? UserName, string? DisplayName, string? Password);

public sealed record LoginRequest(string? UserName, string? Password, bool RememberMe);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record CreateUserRequest(string? UserName, string? DisplayName, string? Role);

public sealed record UpdateUserRequest(string? DisplayName, string? Role, bool Disabled);
