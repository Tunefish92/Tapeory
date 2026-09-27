namespace Tapeory.Api.Data.Entities;

public enum UserRole
{
    /// <summary>Templates, printing and print history.</summary>
    User = 0,

    /// <summary>Everything, including printers, backups, statistics and accounts.</summary>
    Admin = 1
}
