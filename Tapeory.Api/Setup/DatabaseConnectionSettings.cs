using MySqlConnector;

namespace Tapeory.Api.Setup;

/// <summary>
/// The MySQL/MariaDB connection details entered in the first-run setup, and the shape they are
/// stored in on disk (see <see cref="DatabaseConfigStore"/>).
/// </summary>
public sealed record DatabaseConnectionSettings(string Host, int Port, string Database, string User, string Password)
{
    public const int DefaultPort = 3306;

    private const int MaxHostLength = 255;
    private const int MaxDatabaseLength = 64;
    private const int MaxUserLength = 80;
    private const int MaxPasswordLength = 1024;

    /// <summary>Builds the connection string via MySqlConnectionStringBuilder, so values containing
    /// ';', '=' or quotes (common in generated passwords) are escaped correctly.</summary>
    public string ToConnectionString(bool includeDatabase = true, uint? connectTimeoutSeconds = null)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)Port,
            UserID = User,
            Password = Password
        };

        if (includeDatabase)
        {
            builder.Database = Database;
        }

        if (connectTimeoutSeconds is not null)
        {
            builder.ConnectionTimeout = connectTimeoutSeconds.Value;
        }

        return builder.ConnectionString;
    }

    /// <returns>Validation errors keyed by camelCase property name; empty when valid.</returns>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(Host))
        {
            errors["host"] = ["Enter the database server's hostname or IP address."];
        }
        else if (Host.Length > MaxHostLength || Host.Any(char.IsWhiteSpace))
        {
            errors["host"] = ["The server address is not a valid hostname or IP address."];
        }

        if (Port is < 1 or > 65535)
        {
            errors["port"] = ["The port must be between 1 and 65535."];
        }

        if (string.IsNullOrWhiteSpace(Database))
        {
            errors["database"] = ["Enter the database name."];
        }
        else if (Database.Length > MaxDatabaseLength || Database.IndexOfAny(['/', '\\', '.']) >= 0 || Database.EndsWith(' '))
        {
            // MySQL's own rules for database names (they map to directory names on the server).
            errors["database"] = [$"Database names can be at most {MaxDatabaseLength} characters and can't contain '/', '\\' or '.'."];
        }

        if (string.IsNullOrWhiteSpace(User))
        {
            errors["user"] = ["Enter the database username."];
        }
        else if (User.Length > MaxUserLength)
        {
            errors["user"] = [$"The username can be at most {MaxUserLength} characters."];
        }

        if (Password is { Length: > MaxPasswordLength })
        {
            errors["password"] = [$"The password can be at most {MaxPasswordLength} characters."];
        }

        return errors;
    }
}
