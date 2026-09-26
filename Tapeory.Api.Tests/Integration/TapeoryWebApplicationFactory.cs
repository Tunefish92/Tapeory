using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MySql;

namespace Tapeory.Api.Tests.Integration;

/// <summary>
/// Boots the real API host against a MySQL container (via Testcontainers), so integration
/// tests exercise EF Core / Pomelo against an actual server rather than a mocked one.
/// Requires a working Docker daemon on the machine running the tests.
/// </summary>
public sealed class TapeoryWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("tapeory_test")
        .WithUsername("tapeory")
        .WithPassword("tapeory")
        .Build();

    private readonly string _storagePath = Directory.CreateTempSubdirectory("tapeory-tests-").FullName;

    /// <summary>Lets setup tests enter this container's details into an unconfigured host.</summary>
    public string MySqlHost => _mysql.Hostname;

    public int MySqlPort => _mysql.GetMappedPublicPort(MySqlBuilder.MySqlPort);

    public Task InitializeAsync() => _mysql.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _mysql.DisposeAsync();

        if (Directory.Exists(_storagePath))
        {
            Directory.Delete(_storagePath, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _mysql.GetConnectionString(),
                ["TAPEORY_STORAGE_PATH"] = _storagePath,
                ["TAPEORY_AUTO_MIGRATE"] = "true"
            });
        });
    }
}
