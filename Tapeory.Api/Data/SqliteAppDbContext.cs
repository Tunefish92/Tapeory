using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tapeory.Api.Data.Entities;

namespace Tapeory.Api.Data;

/// <summary>
/// The same model on SQLite, for the desktop app's local database, with its own migrations in
/// Data/SqliteMigrations. A few things SQLite does differently are evened out here.
/// </summary>
public sealed class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : AppDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                // The column types above are MySQL's (longtext, decimal(6,2)); SQLite's own
                // mapping is the right one here.
                property.SetColumnType(null);

                // SQLite has no date type, and EF Core can't sort or compare DateTimeOffset
                // stored as text; stored as a number, it can.
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
                }

                // Decimals would be stored as text, which SQLite can't sum or sort; sizes in mm
                // with two decimals are exact enough as numbers.
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    property.SetValueConverter(new CastingConverter<decimal, double>());
                }
            }
        }

        // MySQL's default collation ignores case; SQLite's NOCASE does the same (for ASCII), so
        // "Ada" and "ada" are still one user name and one group.
        modelBuilder.Entity<User>().Property(user => user.UserName).UseCollation("NOCASE");
        modelBuilder.Entity<Template>().Property(template => template.Category).UseCollation("NOCASE");
    }
}

/// <summary>For <c>dotnet ef migrations add … --context SqliteAppDbContext</c>.</summary>
public sealed class SqliteAppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

/// <summary>For <c>dotnet ef migrations add … --context AppDbContext</c>: always the MySQL model. Without
/// it the tools could pick <see cref="SqliteAppDbContextDesignTimeFactory"/>, since the SQLite
/// context is an AppDbContext too.</summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseMySql(Setup.DatabaseSetupService.ServerVersion).Options);
}
