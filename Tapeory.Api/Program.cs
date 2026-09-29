using Tapeory.Api;
using Tapeory.Api.Auth;
using Tapeory.Api.Desktop;

// `--engine`: the engine under the Rust desktop app (see EngineMode).
if (args is [EngineMode.Argument, ..])
{
    return await EngineMode.RunAsync();
}

// `tapeory reset-password <user>` runs a command instead of the server.
var command = args is [ResetPasswordCommand.Name, ..] ? args : null;

var app = TapeoryApp.Build(command is null ? args : []);

if (command is not null)
{
    return await ResetPasswordCommand.RunAsync(app.Services, command);
}

app.Run();
return 0;

// Exposed so WebApplicationFactory<Program> can bootstrap the app in integration tests.
public partial class Program;
