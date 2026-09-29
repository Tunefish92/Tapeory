using System.Security.Cryptography;
using System.Text;

namespace Tapeory.Api.Desktop;

/// <summary>
/// In engine mode (the desktop app), Tapeory's server listens on 127.0.0.1 for the Rust app only.
/// Anything else on the computer could reach that address too, including websites open in a
/// browser, so the desktop app starts the engine with a random secret (<see cref="TokenSetting"/>)
/// and sends it as the <see cref="TokenHeader"/> header with every request; everything without it
/// is refused. The Host header must be the loopback address as well, which stops DNS-rebinding
/// tricks, and a browser can't add the header to a cross-site request without the server's
/// consent, which it never gives.
/// </summary>
public static class DesktopGuard
{
    public const string TokenSetting = "TAPEORY_DESKTOP_TOKEN";
    public const string TokenHeader = "X-Tapeory-Token";

    /// <summary>Whether Tapeory runs as the desktop app's engine.</summary>
    public static bool IsDesktop(IConfiguration configuration) =>
        !string.IsNullOrEmpty(configuration[TokenSetting]);

    public static WebApplication UseDesktopGuard(this WebApplication app)
    {
        var token = app.Configuration[TokenSetting];

        if (string.IsNullOrEmpty(token))
        {
            return app;
        }

        var expected = Encoding.UTF8.GetBytes(token);

        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var presented = request.Headers[TokenHeader].ToString();

            if (request.Host.Host is not ("127.0.0.1" or "localhost")
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), expected))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("This is the engine of Tapeory's desktop app; only the app itself can use it.");
                return;
            }

            await next(context);
        });

        return app;
    }
}
