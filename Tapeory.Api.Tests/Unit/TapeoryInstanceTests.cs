using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Tapeory.Api.Instances;

namespace Tapeory.Api.Tests.Unit;

public sealed class TapeoryInstanceTests : IDisposable
{
    private readonly string _storagePath = Directory.CreateTempSubdirectory("tapeory-instance-").FullName;

    public void Dispose() => Directory.Delete(_storagePath, recursive: true);

    private TapeoryInstance Create()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TAPEORY_STORAGE_PATH"] = _storagePath })
            .Build();
        var storage = new StorageService(configuration, new FakeHostEnvironment(_storagePath));
        return new TapeoryInstance(storage, NullLogger<TapeoryInstance>.Instance);
    }

    [Fact]
    public void KeepsItsId_AcrossRestarts()
    {
        var first = Create();
        var again = Create();

        Assert.Equal(32, first.Id.Length);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(Environment.MachineName, first.Name);
    }

    [Fact]
    public void GetsANewId_WhenTheFileIsDamaged()
    {
        var first = Create();
        File.WriteAllText(Path.Combine(_storagePath, "config", TapeoryInstance.FileName), "{ not json");

        Assert.NotEqual(first.Id, Create().Id);
    }

    [Fact]
    public void Owns_ItsOwnAndUnboundThings_ButNotOthers()
    {
        var instance = Create();

        Assert.True(instance.Owns(instance.Id));
        Assert.True(instance.Owns(null));
        Assert.False(instance.Owns("someone-else"));
    }

    private sealed class FakeHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tapeory.Api.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
