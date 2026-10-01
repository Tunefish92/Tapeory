using Microsoft.EntityFrameworkCore;
using Tapeory.Api.Setup;

namespace Tapeory.Api.Data;

/// <summary>Creates the DbContext for whichever database is configured: MySQL or SQLite.</summary>
public static class DatabaseContexts
{
    public static AppDbContext Create(DatabaseConfigStore database, ILoggerFactory loggerFactory)
    {
        var connectionString = database.ConnectionString;

        if (database.Provider == DatabaseProvider.Sqlite && connectionString is not null)
        {
            return new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>()
                .UseSqlite(connectionString)
                .UseLoggerFactory(loggerFactory)
                .Options);
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseLoggerFactory(loggerFactory);

        // Without a connection the context can still be constructed (and `dotnet ef migrations
        // add` run); any actual query would fail, which the setup-required middleware prevents.
        return new AppDbContext(connectionString is null
            ? options.UseMySql(DatabaseSetupService.ServerVersion).Options
            : options.UseMySql(connectionString, DatabaseSetupService.ServerVersion).Options);
    }
}
