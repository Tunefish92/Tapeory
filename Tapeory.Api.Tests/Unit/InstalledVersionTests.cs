using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Updates;

namespace Tapeory.Api.Tests.Unit;

public sealed class InstalledVersionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public InstalledVersionTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private SqliteAppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public async Task NewInstallation_IsNotAnUpdate()
    {
        await using var db = NewContext();
        await InstalledVersion.MigrateAsync(db, "0.6.0", CancellationToken.None);

        var installed = await InstalledVersion.GetAsync(db, "0.6.0", CancellationToken.None);

        Assert.Equal(new InstalledVersionResponse("0.6.0", false, null, null), installed);
    }

    [Fact]
    public async Task NewVersion_IsAnUpdateFromTheRecordedOne()
    {
        await using (var db = NewContext())
        {
            await InstalledVersion.MigrateAsync(db, "0.6.0", CancellationToken.None);
        }

        await using (var db = NewContext())
        {
            await InstalledVersion.MigrateAsync(db, "0.7.0", CancellationToken.None);
            var installed = await InstalledVersion.GetAsync(db, "0.7.0", CancellationToken.None);

            Assert.True(installed.Updated);
            Assert.Equal("0.6.0", installed.PreviousVersion);
            Assert.NotNull(installed.UpdatedAt);
        }
    }

    [Fact]
    public async Task ExistingDatabaseWithoutARecord_IsAnUpdateFromAnUnknownVersion()
    {
        // What 0.5.0 leaves behind: Tapeory's tables, but no recorded version.
        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
        }

        await using (var db = NewContext())
        {
            await InstalledVersion.MigrateAsync(db, "0.6.0", CancellationToken.None);
            var installed = await InstalledVersion.GetAsync(db, "0.6.0", CancellationToken.None);

            Assert.True(installed.Updated);
            Assert.Null(installed.PreviousVersion);
        }
    }

    [Fact]
    public async Task RestartOnTheSameVersion_KeepsTheUpdate()
    {
        await using (var db = NewContext())
        {
            await InstalledVersion.MigrateAsync(db, "0.6.0", CancellationToken.None);
            await InstalledVersion.MigrateAsync(db, "0.7.0", CancellationToken.None);
        }

        DateTimeOffset? updatedAt;
        await using (var db = NewContext())
        {
            updatedAt = (await InstalledVersion.GetAsync(db, "0.7.0", CancellationToken.None)).UpdatedAt;
            await InstalledVersion.MigrateAsync(db, "0.7.0", CancellationToken.None);
        }

        await using (var db = NewContext())
        {
            var installed = await InstalledVersion.GetAsync(db, "0.7.0", CancellationToken.None);
            Assert.Equal(new InstalledVersionResponse("0.7.0", true, "0.6.0", updatedAt), installed);
        }
    }

    [Fact]
    public async Task DatabaseRecordedByAnotherVersion_IsNotAnUpdateOfThisOne()
    {
        await using var db = NewContext();
        await InstalledVersion.MigrateAsync(db, "0.6.0", CancellationToken.None);
        await InstalledVersion.MigrateAsync(db, "0.7.0", CancellationToken.None);

        var installed = await InstalledVersion.GetAsync(db, "0.6.0", CancellationToken.None);

        Assert.False(installed.Updated);
    }
}
