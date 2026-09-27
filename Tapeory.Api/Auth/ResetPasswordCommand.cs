namespace Tapeory.Api.Auth;

/// <summary>
/// <c>tapeory reset-password &lt;user&gt;</c> on the server (for Docker: <c>docker exec tapeory
/// tapeory reset-password admin</c>): prints a temporary password for an account whose password
/// is lost, for example the only administrator's.
/// </summary>
public static class ResetPasswordCommand
{
    public const string Name = "reset-password";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: tapeory reset-password <user name>");
            return 2;
        }

        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AccountService>()
            .ResetPasswordByNameAsync(args[1], CancellationToken.None);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        Console.WriteLine($"Temporary password for {result.Value.User.UserName}: {result.Value.Password}");
        Console.WriteLine("Sign in with it; Tapeory then asks for a new password.");
        return 0;
    }
}
