using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Tapeory.Api.Data;
using Tapeory.Api.Setup;

namespace Tapeory.Api.Auth;

/// <summary>
/// Whether any account exists yet. Without one, Tapeory stays open, as it was before accounts
/// existed; once the first is created, every API call needs a signed-in account.
/// </summary>
public sealed class UserDirectory(IServiceScopeFactory scopes, DatabaseConfigStore database)
{
    // MySQL's "table doesn't exist": the migration adding accounts hasn't run yet.
    private const int TableMissing = 1146;

    // Accounts can't all be deleted (the last admin stays), so once some exist that holds until
    // a database restore, which calls Reset.
    private volatile bool _hasUsers;

    public async Task<bool> HasUsersAsync(CancellationToken cancellationToken)
    {
        if (_hasUsers)
        {
            return true;
        }

        if (!database.IsConfigured)
        {
            return false;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            _hasUsers = await db.Users.AnyAsync(cancellationToken);
        }
        catch (MySqlException ex) when (ex.Number == TableMissing)
        {
            return false;
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.Ordinal))
        {
            return false;
        }

        return _hasUsers;
    }

    /// <summary>Checks again on the next request, after the accounts may have changed wholesale.</summary>
    public void Reset() => _hasUsers = false;

    /// <summary>Called when the first account is created.</summary>
    public void MarkHasUsers() => _hasUsers = true;
}
