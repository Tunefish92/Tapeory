using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Setup;

namespace Tapeory.Api.Auth;

public static class AuthSetup
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string CookieName = "tapeory.session";

    /// <summary>The header the web UI sends with every change. A form on another site can't set
    /// it, so a change without it (and with a session cookie) didn't come from Tapeory.</summary>
    public const string CsrfHeader = "X-Requested-With";

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public static IServiceCollection AddTapeoryAuth(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<UserDirectory>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<AccountService>();
        services.AddSingleton<IAuthorizationHandler, SignedInHandler>();

        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Most installs run on plain HTTP in a home network; behind HTTPS the cookie is Secure.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = SessionLifetime;
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = ValidateSessionAsync;
            // An API answers with a status code, not a redirect to a login page.
            options.Events.OnRedirectToLogin = context =>
                WriteProblem(context.HttpContext, StatusCodes.Status401Unauthorized, "Please sign in.");
            options.Events.OnRedirectToAccessDenied = context =>
                WriteProblem(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    AuthClaims.MustChange(context.HttpContext.User)
                        ? "Choose a new password first."
                        : "Only administrators can do this.");
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(Policy(openWithoutAccounts: true, adminOnly: false))
            .AddPolicy(AuthPolicies.AdminOrOpen, Policy(openWithoutAccounts: true, adminOnly: true))
            .AddPolicy(AuthPolicies.Admin, Policy(openWithoutAccounts: false, adminOnly: true));

        return services;
    }

    /// <summary>Refuses changes that carry a session cookie but not the web UI's header.</summary>
    public static IApplicationBuilder UseCsrfCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var isChange = !HttpMethods.IsGet(request.Method)
                && !HttpMethods.IsHead(request.Method)
                && !HttpMethods.IsOptions(request.Method);

            if (isChange
                && request.Path.StartsWithSegments("/api")
                && request.Cookies.ContainsKey(CookieName)
                && !request.Headers.ContainsKey(CsrfHeader))
            {
                await WriteProblem(context, StatusCodes.Status403Forbidden, "This request didn't come from Tapeory's web UI.");
                return;
            }

            await next(context);
        });

    private static AuthorizationPolicy Policy(bool openWithoutAccounts, bool adminOnly) =>
        new AuthorizationPolicyBuilder(Scheme)
            .AddRequirements(new SignedInRequirement(openWithoutAccounts, adminOnly))
            .Build();

    /// <summary>
    /// Checks every session against its account: a deleted or disabled account, or a changed
    /// password, ends it; a changed role or name takes effect right away.
    /// </summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var userId = context.Principal is { } principal ? AuthClaims.UserId(principal) : null;

        User? user = null;

        if (userId is not null && services.GetRequiredService<DatabaseConfigStore>().IsConfigured)
        {
            user = await services.GetRequiredService<AppDbContext>().Users
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == userId, context.HttpContext.RequestAborted);
        }

        if (user is null
            || user.Disabled
            || context.Principal!.FindFirst(AuthClaims.SecurityStamp)?.Value != user.SecurityStamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
            return;
        }

        if (!AuthClaims.Matches(context.Principal, user))
        {
            context.ReplacePrincipal(AuthClaims.For(user));
            context.ShouldRenew = true;
        }
    }

    private static Task WriteProblem(HttpContext context, int status, string title) =>
        Results.Problem(title: title, statusCode: status).ExecuteAsync(context);
}
