using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tapeory.Api.Desktop;

namespace Tapeory.Api.Tests.Integration;

/// <summary>
/// Tapeory as the desktop app hosts it: a secret token guards the local server, and the setup
/// offers a local SQLite database. Each test gets its own host and data folder.
/// </summary>
public sealed class DesktopHostTests : IDisposable
{
    private const string Token = "0123456789ABCDEF0123456789ABCDEF";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _dataFolder = Directory.CreateTempSubdirectory("tapeory-desktop-tests-").FullName;
    private readonly WebApplicationFactory<Program> _desktop;

    public DesktopHostTests()
    {
        _desktop = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TAPEORY_STORAGE_PATH"] = _dataFolder,
                    [DesktopGuard.TokenSetting] = Token,
                });
            });
        });
    }

    public void Dispose()
    {
        _desktop.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dataFolder, recursive: true);
    }

    private HttpClient NewClient(string? token = Token)
    {
        var client = _desktop.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        if (token is not null)
        {
            client.DefaultRequestHeaders.Add(DesktopGuard.TokenHeader, token);
        }

        return client;
    }

    [Fact]
    public void NothingIsLoggedToTheWindowsEventLog()
    {
        var providers = _desktop.Services.GetServices<ILoggerProvider>().Select(provider => provider.GetType().Name).ToList();

        Assert.Contains("ConsoleLoggerProvider", providers);
        Assert.DoesNotContain("EventLogLoggerProvider", providers);
    }

    [Fact]
    public async Task WithoutTheSecret_EverythingIsRefused()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await NewClient(token: null).GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await NewClient(token: null).GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await NewClient(token: "wrong").GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task OtherHostNames_AreRefused_EvenWithTheSecret()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Host = "attacker.example";

        Assert.Equal(HttpStatusCode.Forbidden, (await NewClient().SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task TheDesktopApp_SetsUpALocalDatabase_AndWorksWithoutAccounts()
    {
        var client = NewClient();

        var status = await client.GetFromJsonAsync<SetupStatus>("/api/setup/status", JsonOptions);
        Assert.False(status!.Configured);
        Assert.True(status.Desktop);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/setup/database/local", null)).StatusCode);
        Assert.True(File.Exists(Path.Combine(_dataFolder, "tapeory.db")));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/setup/database/local", null)).StatusCode);

        var auth = await client.GetFromJsonAsync<AuthState>("/api/auth/state", JsonOptions);
        Assert.False(auth!.HasUsers);
        Assert.True(auth.Desktop);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/templates")).StatusCode);
    }

    private sealed record SetupStatus(bool Configured, bool Desktop);

    private sealed record AuthState(bool HasUsers, bool Desktop);
}

[Collection(IntegrationTestCollection.Name)]
public sealed class ServerHostTests(TapeoryWebApplicationFactory factory)
{
    [Fact]
    public async Task TheServer_OffersNoLocalDatabase_AndIsNotTheDesktopApp()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/setup/database/local", null)).StatusCode);
        var status = await client.GetFromJsonAsync<JsonElement>("/api/setup/status");
        Assert.False(status.GetProperty("desktop").GetBoolean());
    }
}
