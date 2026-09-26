using Tapeory.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Tapeory.Api.Tests.Unit;

public sealed class StorageServiceTests
{
    private static readonly string[] RequiredDirectories =
    [
        "templates",
        "uploads",
        "images",
        "exports",
        "backups",
        "original-lbx",
        "print-jobs"
    ];

    [Fact]
    public void RootPath_DefaultsToLocalStorageFolderUnderContentRoot_WhenNotConfigured()
    {
        var contentRoot = Directory.CreateTempSubdirectory("tapeory-content-").FullName;
        try
        {
            var configuration = new ConfigurationBuilder().Build();
            var environment = new FakeHostEnvironment(contentRoot);

            var storage = new StorageService(configuration, environment);

            Assert.Equal(Path.Combine(contentRoot, "local-storage"), storage.RootPath);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void RootPath_UsesConfiguredPath_WhenSet()
    {
        var configuredPath = Directory.CreateTempSubdirectory("tapeory-storage-").FullName;
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TAPEORY_STORAGE_PATH"] = configuredPath
                })
                .Build();
            var environment = new FakeHostEnvironment(Path.GetTempPath());

            var storage = new StorageService(configuration, environment);

            Assert.Equal(Path.GetFullPath(configuredPath), storage.RootPath);
        }
        finally
        {
            Directory.Delete(configuredPath, recursive: true);
        }
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("")]
    public void RootPath_FallsBackToDefault_WhenConfiguredPathIsBlank(string blankPath)
    {
        var contentRoot = Directory.CreateTempSubdirectory("tapeory-content-").FullName;
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TAPEORY_STORAGE_PATH"] = blankPath
                })
                .Build();
            var environment = new FakeHostEnvironment(contentRoot);

            var storage = new StorageService(configuration, environment);

            Assert.Equal(Path.Combine(contentRoot, "local-storage"), storage.RootPath);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void EnsureDirectories_CreatesRootAndAllRequiredSubdirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "tapeory-ensure-" + Guid.NewGuid());
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TAPEORY_STORAGE_PATH"] = root
                })
                .Build();
            var environment = new FakeHostEnvironment(Path.GetTempPath());
            var storage = new StorageService(configuration, environment);

            storage.EnsureDirectories();

            Assert.True(Directory.Exists(root));

            foreach (var subdirectory in RequiredDirectories)
            {
                Assert.True(
                    Directory.Exists(Path.Combine(root, subdirectory)),
                    $"Expected storage subdirectory '{subdirectory}' to exist.");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void EnsureDirectories_IsIdempotent_WhenCalledMultipleTimes()
    {
        var root = Path.Combine(Path.GetTempPath(), "tapeory-ensure-" + Guid.NewGuid());
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TAPEORY_STORAGE_PATH"] = root
                })
                .Build();
            var environment = new FakeHostEnvironment(Path.GetTempPath());
            var storage = new StorageService(configuration, environment);

            storage.EnsureDirectories();
            var exception = Record.Exception(() => storage.EnsureDirectories());

            Assert.Null(exception);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tapeory.Api.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
