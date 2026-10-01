using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Tapeory.Api.Desktop;

/// <summary>
/// <c>Tapeory.Api --engine</c>: the engine under the Rust desktop app. It listens on 127.0.0.1 with
/// a free port, only for requests carrying the secret from <c>TAPEORY_ENGINE_TOKEN</c>, keeps its
/// data in <c>TAPEORY_STORAGE_PATH</c>, writes <see cref="ReadyPrefix"/> and its address on a line
/// of its own once it's ready, and stops when its standard input closes, which happens when the
/// desktop app exits or crashes.
/// </summary>
public static class EngineMode
{
    public const string Argument = "--engine";
    public const string ReadyPrefix = "TAPEORY_ENGINE_READY";
    public const string TokenVariable = "TAPEORY_ENGINE_TOKEN";

    public static async Task<int> RunAsync()
    {
        var token = Environment.GetEnvironmentVariable(TokenVariable);
        var storage = Environment.GetEnvironmentVariable("TAPEORY_STORAGE_PATH");

        if (string.IsNullOrWhiteSpace(token) || token.Length < 32 || string.IsNullOrWhiteSpace(storage))
        {
            await Console.Error.WriteLineAsync(
                $"Engine mode needs {TokenVariable} (at least 32 characters) and TAPEORY_STORAGE_PATH.");
            return 2;
        }

        var app = TapeoryApp.Build([], new TapeoryHostOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            Settings = new Dictionary<string, string?>
            {
                ["TAPEORY_STORAGE_PATH"] = storage,
                [DesktopGuard.TokenSetting] = token,
                // Port 0: the system picks a free one.
                ["urls"] = "http://127.0.0.1:0",
            },
        });

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        await Console.Out.WriteLineAsync($"{ReadyPrefix} {address}");
        await Console.Out.FlushAsync();

        // The desktop app holds our standard input open for as long as it runs.
        _ = Task.Run(async () =>
        {
            try
            {
                await Console.In.ReadToEndAsync();
            }
            catch (IOException)
            {
            }

            app.Lifetime.StopApplication();
        });

        await app.WaitForShutdownAsync();
        return 0;
    }
}
