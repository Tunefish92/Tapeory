using Tapeory.Api.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace Tapeory.Api.Tests.Unit;

public sealed class DatabaseConfigStoreTests : IDisposable
{
    private static readonly DatabaseConnectionSettings ValidSettings =
        new("db.local", 3307, "tapeory", "tapeory", "p;ss=\"word'");

    private readonly string _storagePath = Directory.CreateTempSubdirectory("tapeory-dbconfig-").FullName;

    public void Dispose() => Directory.Delete(_storagePath, recursive: true);

    [Fact]
    public void IsNotConfigured_OnFreshInstall()
    {
        var store = CreateStore();

        Assert.False(store.IsConfigured);
        Assert.Null(store.ConnectionString);
        Assert.Equal(Path.Combine(_storagePath, "config", "database.json"), store.FilePath);
    }

    [Fact]
    public void Save_AppliesImmediately_AndIsLoadedOnNextStart()
    {
        var store = CreateStore();

        store.Save(ValidSettings);

        Assert.True(store.IsConfigured);
        Assert.True(File.Exists(store.FilePath));

        var reloaded = CreateStore();
        Assert.Equal(store.ConnectionString, reloaded.ConnectionString);
        Assert.NotNull(reloaded.ConnectionString);

        var parsed = new MySqlConnectionStringBuilder(reloaded.ConnectionString);
        Assert.Equal("db.local", parsed.Server);
        Assert.Equal(3307u, parsed.Port);
        Assert.Equal("tapeory", parsed.Database);
        Assert.Equal("tapeory", parsed.UserID);
        Assert.Equal("p;ss=\"word'", parsed.Password);
    }

    [Fact]
    public void Save_RestrictsFileToOwner_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var store = CreateStore();
        store.Save(ValidSettings);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(store.FilePath));
    }

    [Fact]
    public void ConfiguredConnectionString_OverridesTheFile()
    {
        CreateStore().Save(ValidSettings);

        var store = CreateStore(connectionString: "server=elsewhere;database=x;user=y;password=z;");

        Assert.Equal("server=elsewhere;database=x;user=y;password=z;", store.ConnectionString);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "host": "", "port": 3306, "database": "tapeory", "user": "tapeory", "password": "" }""")]
    public void InvalidFile_IsIgnored_SoSetupRunsAgain(string contents)
    {
        var path = Path.Combine(_storagePath, "config", "database.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);

        Assert.False(CreateStore().IsConfigured);
    }

    [Theory]
    [InlineData("", 3306, "tapeory", "tapeory", "host")]
    [InlineData("my host", 3306, "tapeory", "tapeory", "host")]
    [InlineData("db", 0, "tapeory", "tapeory", "port")]
    [InlineData("db", 70000, "tapeory", "tapeory", "port")]
    [InlineData("db", 3306, "", "tapeory", "database")]
    [InlineData("db", 3306, "tape.ory", "tapeory", "database")]
    [InlineData("db", 3306, "tapeory", " ", "user")]
    public void Validate_RejectsInvalidSettings(string host, int port, string database, string user, string expectedField)
    {
        var errors = new DatabaseConnectionSettings(host, port, database, user, "").Validate();

        Assert.Equal([expectedField], errors.Keys);
    }

    [Fact]
    public void Validate_AcceptsEmptyPassword()
    {
        Assert.Empty((ValidSettings with { Password = "" }).Validate());
    }

    [Fact]
    public void Request_TrimsValues_AndDefaultsThePort()
    {
        var settings = new DatabaseSetupRequest(" db ", null, " tapeory ", " user ", " secret ").ToSettings();

        Assert.Equal(new DatabaseConnectionSettings("db", 3306, "tapeory", "user", " secret "), settings);
    }

    private DatabaseConfigStore CreateStore(string? connectionString = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TAPEORY_STORAGE_PATH"] = _storagePath,
                ["ConnectionStrings:Default"] = connectionString
            })
            .Build();

        var storage = new StorageService(configuration, new FakeHostEnvironment(_storagePath));
        return new DatabaseConfigStore(configuration, storage, NullLogger<DatabaseConfigStore>.Instance);
    }

    private sealed class FakeHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tapeory.Api.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
