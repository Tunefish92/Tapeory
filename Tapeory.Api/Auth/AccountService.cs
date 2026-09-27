using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Auth;

public enum AccountError
{
    None,
    Invalid,
    Conflict,
    NotFound,
    WrongPassword,
    Throttled
}

public sealed record AccountResult<T>(T? Value, AccountError Error = AccountError.None, string? Message = null, TimeSpan? RetryAfter = null)
{
    public bool Succeeded => Error == AccountError.None;

    public static AccountResult<T> Ok(T value) => new(value);

    public static AccountResult<T> Fail(AccountError error, string message, TimeSpan? retryAfter = null) =>
        new(default, error, message, retryAfter);
}

/// <summary>Creating accounts, signing in, passwords, and the rules that keep an admin around.</summary>
public sealed class AccountService(
    AppDbContext db,
    IPasswordHasher<User> hasher,
    UserDirectory directory,
    LoginThrottle throttle,
    TimeProvider time)
{
    // One Tapeory process serves an installation, so this keeps two people from both becoming
    // the first admin at the same moment.
    private static readonly SemaphoreSlim FirstUserLock = new(1, 1);

    private const string WrongCredentials = "Wrong user name or password.";

    public async Task<AccountResult<User>> CreateFirstUserAsync(FirstUserRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.UserName, request.DisplayName) is { } error)
        {
            return AccountResult<User>.Fail(AccountError.Invalid, error);
        }

        if (AccountRules.PasswordError(request.Password) is { } passwordError)
        {
            return AccountResult<User>.Fail(AccountError.Invalid, passwordError);
        }

        await FirstUserLock.WaitAsync(cancellationToken);

        try
        {
            if (await db.Users.AnyAsync(cancellationToken))
            {
                return AccountResult<User>.Fail(AccountError.Conflict, "An account already exists. Please sign in.");
            }

            var userName = request.UserName!.Trim();
            var user = new User
            {
                UserName = userName,
                DisplayName = AccountRules.DisplayNameOrUserName(request.DisplayName, userName),
                Role = UserRole.Admin,
                CreatedAt = time.GetUtcNow(),
                LastLoginAt = time.GetUtcNow(),
            };
            user.PasswordHash = hasher.HashPassword(user, request.Password!);

            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            directory.MarkHasUsers();
            return AccountResult<User>.Ok(user);
        }
        finally
        {
            FirstUserLock.Release();
        }
    }

    public async Task<AccountResult<User>> SignInAsync(LoginRequest request, string? address, CancellationToken cancellationToken)
    {
        var userName = request.UserName?.Trim() ?? string.Empty;

        if (throttle.RetryAfter(address, userName) is { } wait)
        {
            return Throttled(wait);
        }

        var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.UserName == userName, cancellationToken);

        if (user is null || request.Password is null || !VerifyPassword(user, request.Password))
        {
            throttle.RecordFailure(address, userName);
            return AccountResult<User>.Fail(AccountError.WrongPassword, WrongCredentials);
        }

        throttle.RecordSuccess(address, userName);

        if (user.Disabled)
        {
            return AccountResult<User>.Fail(AccountError.WrongPassword, "This account is disabled. Ask an administrator.");
        }

        user.LastLoginAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return AccountResult<User>.Ok(user);
    }

    public async Task<AccountResult<User>> ChangePasswordAsync(
        int userId,
        ChangePasswordRequest request,
        string? address,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FindAsync([userId], cancellationToken);

        if (user is null)
        {
            return AccountResult<User>.Fail(AccountError.NotFound, "The account no longer exists.");
        }

        if (throttle.RetryAfter(address, user.UserName) is { } wait)
        {
            return Throttled(wait);
        }

        if (request.CurrentPassword is null || !VerifyPassword(user, request.CurrentPassword))
        {
            throttle.RecordFailure(address, user.UserName);
            return AccountResult<User>.Fail(AccountError.WrongPassword, "The current password is wrong.");
        }

        if (AccountRules.PasswordError(request.NewPassword) is { } passwordError)
        {
            return AccountResult<User>.Fail(AccountError.Invalid, passwordError);
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return AccountResult<User>.Fail(AccountError.Invalid, "Choose a password different from the current one.");
        }

        throttle.RecordSuccess(address, user.UserName);
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword!);
        user.MustChangePassword = false;
        user.SecurityStamp = User.NewSecurityStamp();
        await db.SaveChangesAsync(cancellationToken);
        return AccountResult<User>.Ok(user);
    }

    public async Task<List<User>> ListAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().OrderBy(user => user.UserName).ToListAsync(cancellationToken);

    public async Task<AccountResult<(User User, string Password)>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request.UserName, request.DisplayName) is { } error)
        {
            return Fail<(User, string)>(AccountError.Invalid, error);
        }

        if (!AccountRules.TryParseRole(request.Role, out var role))
        {
            return Fail<(User, string)>(AccountError.Invalid, "The role must be Admin or User.");
        }

        var userName = request.UserName!.Trim();

        if (await db.Users.AnyAsync(user => user.UserName == userName, cancellationToken))
        {
            return Fail<(User, string)>(AccountError.Conflict, $"An account named {userName} already exists.");
        }

        var password = AccountRules.NewTemporaryPassword();
        var created = new User
        {
            UserName = userName,
            DisplayName = AccountRules.DisplayNameOrUserName(request.DisplayName, userName),
            Role = role,
            MustChangePassword = true,
            CreatedAt = time.GetUtcNow(),
        };
        created.PasswordHash = hasher.HashPassword(created, password);

        db.Users.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return AccountResult<(User, string)>.Ok((created, password));
    }

    public async Task<AccountResult<User>> UpdateAsync(int id, int currentUserId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (AccountRules.DisplayNameError(request.DisplayName) is { } error)
        {
            return AccountResult<User>.Fail(AccountError.Invalid, error);
        }

        if (!AccountRules.TryParseRole(request.Role, out var role))
        {
            return AccountResult<User>.Fail(AccountError.Invalid, "The role must be Admin or User.");
        }

        var user = await db.Users.FindAsync([id], cancellationToken);

        if (user is null)
        {
            return AccountResult<User>.Fail(AccountError.NotFound, "The account no longer exists.");
        }

        if (id == currentUserId && (request.Disabled || role != UserRole.Admin))
        {
            return AccountResult<User>.Fail(AccountError.Invalid, "You can't disable your own account or take away your own admin role.");
        }

        var staysActiveAdmin = role == UserRole.Admin && !request.Disabled;

        if (!staysActiveAdmin && await IsLastActiveAdminAsync(user, cancellationToken))
        {
            return AccountResult<User>.Fail(AccountError.Invalid, "Tapeory needs at least one active administrator.");
        }

        if (request.Disabled && !user.Disabled)
        {
            user.SecurityStamp = User.NewSecurityStamp();
        }

        user.DisplayName = AccountRules.DisplayNameOrUserName(request.DisplayName, user.UserName);
        user.Role = role;
        user.Disabled = request.Disabled;
        await db.SaveChangesAsync(cancellationToken);
        return AccountResult<User>.Ok(user);
    }

    public async Task<AccountResult<(User User, string Password)>> ResetPasswordAsync(int id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FindAsync([id], cancellationToken);

        if (user is null)
        {
            return Fail<(User, string)>(AccountError.NotFound, "The account no longer exists.");
        }

        var password = SetTemporaryPassword(user);
        await db.SaveChangesAsync(cancellationToken);
        return AccountResult<(User, string)>.Ok((user, password));
    }

    /// <summary>Deletes an account. Its templates go to the administrator deleting it (staying
    /// private), or are deleted with it when <paramref name="deleteTemplates"/> is set. Its print
    /// jobs stay in the history under its name.</summary>
    public async Task<AccountResult<bool>> DeleteAsync(
        int id,
        int currentUserId,
        CancellationToken cancellationToken,
        bool deleteTemplates = false)
    {
        if (id == currentUserId)
        {
            return AccountResult<bool>.Fail(AccountError.Invalid, "You can't delete your own account.");
        }

        var user = await db.Users.FindAsync([id], cancellationToken);

        if (user is null)
        {
            return AccountResult<bool>.Fail(AccountError.NotFound, "The account no longer exists.");
        }

        if (await IsLastActiveAdminAsync(user, cancellationToken))
        {
            return AccountResult<bool>.Fail(AccountError.Invalid, "Tapeory needs at least one active administrator.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var owned = db.Templates.Where(template => template.OwnerUserId == id && template.DeletedAt == null);

        if (deleteTemplates)
        {
            // The same soft delete as the templates page: print history keeps its template names.
            var now = time.GetUtcNow();
            await owned.ExecuteUpdateAsync(
                setters => setters.SetProperty(template => template.DeletedAt, now),
                cancellationToken);
        }
        else
        {
            await owned.ExecuteUpdateAsync(
                setters => setters.SetProperty(template => template.OwnerUserId, currentUserId),
                cancellationToken);
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountResult<bool>.Ok(true);
    }

    /// <summary>For the command line: a new temporary password for an account that's locked out.</summary>
    public async Task<AccountResult<(User User, string Password)>> ResetPasswordByNameAsync(string userName, CancellationToken cancellationToken)
    {
        var trimmed = userName.Trim();
        var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.UserName == trimmed, cancellationToken);

        if (user is null)
        {
            return Fail<(User, string)>(AccountError.NotFound, $"There's no account named {trimmed}.");
        }

        var password = SetTemporaryPassword(user);
        // Locked out usually means locked out of the only admin: bring it back fully.
        user.Disabled = false;
        await db.SaveChangesAsync(cancellationToken);
        return AccountResult<(User, string)>.Ok((user, password));
    }

    private string SetTemporaryPassword(User user)
    {
        var password = AccountRules.NewTemporaryPassword();
        user.PasswordHash = hasher.HashPassword(user, password);
        user.MustChangePassword = true;
        user.SecurityStamp = User.NewSecurityStamp();
        return password;
    }

    private bool VerifyPassword(User user, string password)
    {
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
        }

        return result != PasswordVerificationResult.Failed;
    }

    private async Task<bool> IsLastActiveAdminAsync(User user, CancellationToken cancellationToken) =>
        user.Role == UserRole.Admin
        && !user.Disabled
        && !await db.Users.AnyAsync(
            other => other.Id != user.Id && other.Role == UserRole.Admin && !other.Disabled,
            cancellationToken);

    private static string? Validate(string? userName, string? displayName) =>
        AccountRules.UserNameError(userName) ?? AccountRules.DisplayNameError(displayName);

    private static AccountResult<User> Throttled(TimeSpan wait) =>
        AccountResult<User>.Fail(
            AccountError.Throttled,
            $"Too many wrong passwords. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes.",
            wait);

    private static AccountResult<T> Fail<T>(AccountError error, string message) => AccountResult<T>.Fail(error, message);
}
