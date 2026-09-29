using System.Text.Json;

namespace Tapeory.Api.Instances;

/// <summary>
/// This installation of Tapeory, with an id kept in <c>config/instance.json</c> in the storage
/// folder. Several installations can share one database (say, the desktop app and a Docker
/// server): a print job is only printed by the installation that created it, and a USB printer
/// only by the computer it's connected to.
/// </summary>
public sealed class TapeoryInstance
{
    public const string FileName = "instance.json";

    public TapeoryInstance(StorageService storage, ILogger<TapeoryInstance> logger)
    {
        var path = Path.Combine(storage.RootPath, "config", FileName);
        Name = Environment.MachineName;

        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { Id.Length: > 0 } stored)
            {
                Id = stored.Id;
                return;
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "{Path} couldn't be read; this installation gets a new id.", path);
        }

        Id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Stored(Id)));
    }

    public string Id { get; }

    /// <summary>The computer's name, shown for printers connected to it.</summary>
    public string Name { get; }

    /// <summary>Whether something bound to <paramref name="instanceId"/> is this installation's;
    /// unbound (null) counts as everyone's.</summary>
    public bool Owns(string? instanceId) => instanceId is null || instanceId == Id;

    private sealed record Stored(string Id);
}
