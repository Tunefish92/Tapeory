using System.Diagnostics;
using System.Net;
using Tapeory.Api.Desktop;

namespace Tapeory.Api.Tests.Integration;

/// <summary>The real engine process, started the way the Rust desktop app starts it.</summary>
public sealed class EngineModeTests : IDisposable
{
    private readonly string _dataFolder = Directory.CreateTempSubdirectory("tapeory-engine-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
        catch (IOException)
        {
            // The engine may still be letting go of the database file.
        }
    }

    private Process Start(string? token)
    {
        var start = new ProcessStartInfo("dotnet", [Path.Combine(AppContext.BaseDirectory, "Tapeory.Api.dll"), EngineMode.Argument])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["TAPEORY_STORAGE_PATH"] = _dataFolder;
        start.Environment.Remove("ConnectionStrings__Default");

        if (token is not null)
        {
            start.Environment[EngineMode.TokenVariable] = token;
        }

        return Process.Start(start)!;
    }

    [Fact]
    public async Task StartsOnALoopbackPort_GuardsIt_AndStopsWhenItsInputCloses()
    {
        var token = new string('a', 64);
        using var engine = Start(token);

        string? address = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        while (address is null && await engine.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith(EngineMode.ReadyPrefix, StringComparison.Ordinal))
            {
                address = line[(EngineMode.ReadyPrefix.Length + 1)..];
            }
        }

        Assert.NotNull(address);
        Assert.StartsWith("http://127.0.0.1:", address);

        using var http = new HttpClient { BaseAddress = new Uri(address) };
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/setup/status")).StatusCode);

        http.DefaultRequestHeaders.Add(DesktopGuard.TokenHeader, token);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/setup/status")).StatusCode);

        engine.StandardInput.Close();
        await engine.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        Assert.Equal(0, engine.ExitCode);
    }

    [Fact]
    public async Task RefusesToStart_WithoutASecret()
    {
        using var engine = Start(token: null);

        await engine.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        Assert.Equal(2, engine.ExitCode);
        Assert.Contains(EngineMode.TokenVariable, await engine.StandardError.ReadToEndAsync());
    }
}
