using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tapeory.Api.Tests.Integration;

/// <summary>
/// Runs a second, unconfigured host (no connection string, empty storage folder) and walks it
/// through the first-run database setup against the shared MySQL container.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SetupControllerTests(TapeoryWebApplicationFactory configured) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _storagePath = Directory.CreateTempSubdirectory("tapeory-setup-tests-").FullName;
    private WebApplicationFactory<Program>? _unconfigured;

    public void Dispose()
    {
        _unconfigured?.Dispose();
        Directory.Delete(_storagePath, recursive: true);
    }

    [Fact]
    public async Task BeforeSetup_OnlySetupAndHealthWork()
    {
        var client = CreateUnconfiguredClient();

        var status = await client.GetFromJsonAsync<SetupStatus>("/api/setup/status", JsonOptions);
        Assert.False(status!.Configured);

        var templates = await client.GetAsync("/api/templates");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, templates.StatusCode);
        using var problem = JsonDocument.Parse(await templates.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("setupRequired").GetBoolean());

        var health = await client.GetFromJsonAsync<JsonElement>("/api/health", JsonOptions);
        Assert.False(health.GetProperty("databaseConfigured").GetBoolean());
        Assert.False(health.GetProperty("databaseConnected").GetBoolean());
    }

    [Fact]
    public async Task TestConnection_ReportsWrongPassword()
    {
        var client = CreateUnconfiguredClient();

        var response = await client.PostAsJsonAsync("/api/setup/database/test", Request(password: "wrong"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TestResult>(JsonOptions);
        Assert.False(result!.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task InvalidDetails_AreRejected()
    {
        var client = CreateUnconfiguredClient();

        var response = await client.PostAsJsonAsync("/api/setup/database", Request() with { Host = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await client.GetFromJsonAsync<SetupStatus>("/api/setup/status", JsonOptions))!.Configured);
    }

    [Fact]
    public async Task Setup_SavesTheConnection_UnlocksTheApi_AndLocksItself()
    {
        var client = CreateUnconfiguredClient();

        var test = await client.PostAsJsonAsync("/api/setup/database/test", Request());
        Assert.True((await test.Content.ReadFromJsonAsync<TestResult>(JsonOptions))!.IsSuccess);

        var save = await client.PostAsJsonAsync("/api/setup/database", Request());
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        Assert.True((await client.GetFromJsonAsync<SetupStatus>("/api/setup/status", JsonOptions))!.Configured);
        Assert.True(File.Exists(Path.Combine(_storagePath, "config", "database.json")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/templates")).StatusCode);

        var again = await client.PostAsJsonAsync("/api/setup/database", Request());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/setup/database/test", Request())).StatusCode);
    }

    private SetupRequest Request(string password = "tapeory") =>
        new(configured.MySqlHost, configured.MySqlPort, "tapeory_test", "tapeory", password);

    private HttpClient CreateUnconfiguredClient()
    {
        _unconfigured = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TAPEORY_STORAGE_PATH"] = _storagePath,
                    ["TAPEORY_AUTO_MIGRATE"] = "true"
                });
            });
        });

        return _unconfigured.CreateClient();
    }

    private sealed record SetupRequest(string Host, int Port, string Database, string User, string Password);

    private sealed record SetupStatus(bool Configured);

    private sealed record TestResult(bool IsSuccess, string? ErrorMessage, bool DatabaseExists);
}
