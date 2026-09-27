using Microsoft.AspNetCore.Authorization;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Auth;

public static class AuthPolicies
{
    /// <summary>Admins only, or anyone while no account exists yet: printers, backups, statistics.</summary>
    public const string AdminOrOpen = "AdminOrOpen";

    /// <summary>Signed-in admins only, even without accounts: managing accounts.</summary>
    public const string Admin = "Admin";
}

/// <summary>
/// Who may call an endpoint. While no account exists, <see cref="OpenWithoutAccounts"/> lets
/// everyone in. Otherwise it takes a signed-in account that has chosen its own password (an
/// account on a temporary password can only change it), and an admin for <see cref="AdminOnly"/>.
/// </summary>
public sealed record SignedInRequirement(bool OpenWithoutAccounts, bool AdminOnly) : IAuthorizationRequirement;

public sealed class SignedInHandler(UserDirectory users, IHttpContextAccessor http)
    : AuthorizationHandler<SignedInRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SignedInRequirement requirement)
    {
        var cancellationToken = http.HttpContext?.RequestAborted ?? CancellationToken.None;

        if (requirement.OpenWithoutAccounts && !await users.HasUsersAsync(cancellationToken))
        {
            context.Succeed(requirement);
            return;
        }

        var user = context.User;

        if (user.Identity?.IsAuthenticated == true
            && !AuthClaims.MustChange(user)
            && (!requirement.AdminOnly || user.IsInRole(nameof(UserRole.Admin))))
        {
            context.Succeed(requirement);
        }
    }
}
