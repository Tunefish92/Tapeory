using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tapeory.Api.Auth;
using Tapeory.Api.Data;
using Tapeory.Api.PrintJobs;
using Tapeory.Api.Templates;

namespace Tapeory.Api.Tests.Integration;

/// <summary>
/// Accounts. The database is shared with every other integration test, which expect Tapeory to be
/// open (no accounts), so each test here removes the accounts it created afterwards.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuthTests(TapeoryWebApplicationFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string AdminPassword = "correct horse battery";

    public Task InitializeAsync() => RemoveAccountsAsync();

    public Task DisposeAsync() => RemoveAccountsAsync();

    private async Task RemoveAccountsAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.ExecuteDeleteAsync();
        factory.Services.GetRequiredService<UserDirectory>().Reset();
    }

    [Fact]
    public void SessionKeys_AreKeptInTheStorageFolder_SoSignInsSurviveANewContainer()
    {
        var protector = factory.Services
            .GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
            .CreateProtector("test");
        _ = protector.Protect([1, 2, 3]); // creates the key, as the first sign-in does

        var keys = Path.Combine(factory.Services.GetRequiredService<StorageService>().RootPath, "config", "keys");

        Assert.NotEmpty(Directory.GetFiles(keys, "key-*.xml"));
    }

    /// <summary>A client like the web UI: it keeps the session cookie and sends the CSRF header.</summary>
    private HttpClient NewClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthSetup.CsrfHeader, "Tapeory");
        return client;
    }

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<(HttpClient Client, AuthStateResponse State)> CreateFirstAdminAsync(string? userName = null)
    {
        var client = NewClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/first-user",
            new FirstUserRequest(userName ?? UniqueName("admin"), "The Admin", AdminPassword));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (client, (await response.Content.ReadFromJsonAsync<AuthStateResponse>(JsonOptions))!);
    }

    private async Task<(HttpClient Client, TemporaryPasswordResponse Created)> CreateUserAsync(
        HttpClient admin,
        string role,
        string newPassword = "users own password")
    {
        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(UniqueName("user"), "Some User", role));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<TemporaryPasswordResponse>(JsonOptions))!;

        var client = NewClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, created.User.UserName, created.TemporaryPassword)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/api/auth/password", new ChangePasswordRequest(created.TemporaryPassword, newPassword))).StatusCode);
        return (client, created);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password, bool rememberMe = false) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password, rememberMe));

    private static async Task<AuthStateResponse> StateAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AuthStateResponse>("/api/auth/state", JsonOptions))!;

    [Fact]
    public async Task WithoutAccounts_EverythingStaysOpen()
    {
        var client = factory.CreateClient();

        var state = await StateAsync(client);

        Assert.False(state.HasUsers);
        Assert.Null(state.User);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/backups/database")).StatusCode);
    }

    [Fact]
    public async Task FirstAccount_IsAdmin_SignsIn_AndClosesTheApp()
    {
        var (admin, state) = await CreateFirstAdminAsync();

        Assert.True(state.HasUsers);
        Assert.Equal("Admin", state.User!.Role);
        Assert.Equal("The Admin", state.User.DisplayName);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/templates")).StatusCode);

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/backups/database")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/setup/status")).StatusCode);

        var anonymousState = await StateAsync(anonymous);
        Assert.True(anonymousState.HasUsers);
        Assert.Null(anonymousState.User);
    }

    [Fact]
    public async Task FirstAccount_CanOnlyBeCreatedOnce_EvenAtTheSameMoment()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            NewClient().PostAsJsonAsync(
                "/api/auth/first-user",
                new FirstUserRequest(UniqueName("race"), null, AdminPassword))));

        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
        Assert.All(
            attempts.Where(response => response.StatusCode != HttpStatusCode.OK),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
    }

    [Fact]
    public async Task FirstAccount_RejectsAShortPasswordOrBadName()
    {
        var client = NewClient();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/first-user", new FirstUserRequest("admin", null, "short"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/first-user", new FirstUserRequest("a b", null, AdminPassword))).StatusCode);
        Assert.False((await StateAsync(client)).HasUsers);
    }

    [Fact]
    public async Task Login_ChecksThePassword_AndLogoutEndsTheSession()
    {
        var name = UniqueName("admin");
        await CreateFirstAdminAsync(name);
        var client = NewClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync(client, name, "wrong password")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/templates")).StatusCode);

        // User names ignore case.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, name.ToUpperInvariant(), AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/templates")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/templates")).StatusCode);
    }

    [Fact]
    public async Task Login_IsRefusedForAWhile_AfterTooManyWrongPasswords()
    {
        var name = UniqueName("admin");
        await CreateFirstAdminAsync(name);
        var client = NewClient();

        for (var attempt = 0; attempt < LoginThrottle.MaxFailuresPerAccount; attempt++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync(client, name, "wrong password")).StatusCode);
        }

        var refused = await LoginAsync(client, name, AdminPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter);
    }

    [Fact]
    public async Task UserRole_CanPrint_ButNotManagePrintersBackupsOrAccounts()
    {
        var (admin, _) = await CreateFirstAdminAsync();
        var (user, _) = await CreateUserAsync(admin, "User");

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/printers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/stats")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/printers", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/backups/database")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/stats/reset", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/users")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/backups/database")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task ManagingAccounts_NeedsASignedInAdmin_EvenWithoutAccounts()
    {
        var anonymous = NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/users", new CreateUserRequest("sneaky", null, "Admin"))).StatusCode);
    }

    [Fact]
    public async Task NewAccount_MustChooseItsOwnPassword_BeforeDoingAnythingElse()
    {
        var (admin, _) = await CreateFirstAdminAsync();
        var created = await (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(UniqueName("new"), null, "User")))
            .Content.ReadFromJsonAsync<TemporaryPasswordResponse>(JsonOptions);
        Assert.True(created!.User.MustChangePassword);
        Assert.Equal(created.User.UserName, created.User.DisplayName);

        var client = NewClient();
        var login = await (await LoginAsync(client, created.User.UserName, created.TemporaryPassword))
            .Content.ReadFromJsonAsync<AuthStateResponse>(JsonOptions);
        Assert.True(login!.User!.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/templates")).StatusCode);

        var changed = await client.PutAsJsonAsync(
            "/api/auth/password",
            new ChangePasswordRequest(created.TemporaryPassword, "my own password"));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await changed.Content.ReadFromJsonAsync<AuthStateResponse>(JsonOptions))!.User!.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/templates")).StatusCode);
    }

    [Fact]
    public async Task ChangingYourPassword_NeedsTheCurrentOne_AndEndsYourOtherSessions()
    {
        var name = UniqueName("admin");
        var (client, _) = await CreateFirstAdminAsync(name);
        var otherDevice = NewClient();
        await LoginAsync(otherDevice, name, AdminPassword);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/auth/password", new ChangePasswordRequest("not it", "a new password"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/auth/password", new ChangePasswordRequest(AdminPassword, "short"))).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync("/api/auth/password", new ChangePasswordRequest(AdminPassword, "a new password"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync(NewClient(), name, AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(NewClient(), name, "a new password")).StatusCode);
    }

    [Fact]
    public async Task ChangingPassword_NeedsASession()
    {
        await CreateFirstAdminAsync();

        var response = await NewClient().PutAsJsonAsync("/api/auth/password", new ChangePasswordRequest("x", "a new password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DisablingAnAccount_EndsItsSession_AndStopsItSigningIn()
    {
        var (admin, _) = await CreateFirstAdminAsync();
        var (user, created) = await CreateUserAsync(admin, "User");

        var disabled = await admin.PutAsJsonAsync($"/api/users/{created.User.Id}", new UpdateUserRequest("Some User", "User", true));
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync(NewClient(), created.User.UserName, "users own password")).StatusCode);
    }

    [Fact]
    public async Task ChangingARole_TakesEffectRightAway()
    {
        var (admin, _) = await CreateFirstAdminAsync();
        var (user, created) = await CreateUserAsync(admin, "User");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/users")).StatusCode);

        await admin.PutAsJsonAsync($"/api/users/{created.User.Id}", new UpdateUserRequest("Promoted", "Admin", false));

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/users")).StatusCode);
        var state = await StateAsync(user);
        Assert.Equal("Admin", state.User!.Role);
        Assert.Equal("Promoted", state.User.DisplayName);
    }

    [Fact]
    public async Task ResettingAPassword_EndsTheSession_AndForcesANewPassword()
    {
        var (admin, _) = await CreateFirstAdminAsync();
        var (user, created) = await CreateUserAsync(admin, "User");

        var reset = await (await admin.PostAsync($"/api/users/{created.User.Id}/reset-password", null))
            .Content.ReadFromJsonAsync<TemporaryPasswordResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user, created.User.UserName, reset!.TemporaryPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/templates")).StatusCode);
    }

    [Fact]
    public async Task TheLastAdmin_CanNotBeRemovedDemotedOrDisabled()
    {
        var (admin, state) = await CreateFirstAdminAsync();
        var self = state.User!.Id;

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/users/{self}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"/api/users/{self}", new UpdateUserRequest(null, "User", false))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"/api/users/{self}", new UpdateUserRequest(null, "Admin", true))).StatusCode);

        // With a second admin, one of them can go (just not yourself).
        var (other, created) = await CreateUserAsync(admin, "Admin");
        Assert.Equal(HttpStatusCode.NoContent, (await other.DeleteAsync($"/api/users/{self}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.DeleteAsync($"/api/users/{created.User.Id}")).StatusCode);
    }

    [Fact]
    public async Task UserNames_AreUnique_IgnoringCase()
    {
        var (admin, _) = await CreateFirstAdminAsync("Ada-admin");

        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("ADA-ADMIN", null, "User"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Changes_WithASessionButWithoutTheWebUiHeader_AreRefused()
    {
        var (admin, _) = await CreateFirstAdminAsync();
        admin.DefaultRequestHeaders.Remove(AuthSetup.CsrfHeader);

        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(UniqueName("csrf"), null, "User"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task PrintJobs_RecordWhoPrintedThem()
    {
        var (admin, _) = await CreateFirstAdminAsync();

        var template = await (await admin.PostAsJsonAsync(
                "/api/templates",
                new CreateTemplateRequest(
                    $"Auth-{Guid.NewGuid():N}", null, null, null, 40m, 20m,
                    """{"formatVersion":1,"widthMm":40,"heightMm":20,"objects":[]}""", null),
                JsonOptions))
            .Content.ReadFromJsonAsync<TemplateDetailResponse>(JsonOptions);
        var job = await (await admin.PostAsJsonAsync(
                "/api/print-jobs",
                new CreatePrintJobRequest(template!.Id, null, null, [new PrintJobItemRequest([], 1)]),
                JsonOptions))
            .Content.ReadFromJsonAsync<PrintJobResponse>(JsonOptions);

        Assert.Equal("The Admin", job!.PrintedBy);
    }

    [Fact]
    public async Task ResetPasswordCommand_GivesALockedOutAdminATemporaryPassword()
    {
        var name = UniqueName("admin");
        await CreateFirstAdminAsync(name);

        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var result = await accounts.ResetPasswordByNameAsync(name.ToUpperInvariant(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var client = NewClient();
        var login = await (await LoginAsync(client, name, result.Value.Password)).Content.ReadFromJsonAsync<AuthStateResponse>(JsonOptions);
        Assert.True(login!.User!.MustChangePassword);

        Assert.Equal(1, await ResetPasswordCommand.RunAsync(factory.Services, ["reset-password", "nobody-here"]));
        Assert.Equal(2, await ResetPasswordCommand.RunAsync(factory.Services, ["reset-password"]));
    }
}
