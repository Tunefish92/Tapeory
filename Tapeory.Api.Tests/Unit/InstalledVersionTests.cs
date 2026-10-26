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

        Assert.Equal(new InstalledVersionResponse("0.6.0", false, null, null, null), installed);
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
            // SQLite databases began with 0.5.0.
            Assert.Equal("0.5.0", installed.ChangesSince);
        }
    }

    [Fact]
    public async Task UpdateOverSeveralVersions_ShowsTheChangesSinceTheVersionThatRanLast()
    {
        await using var db = NewContext();
        await InstalledVersion.MigrateAsync(db, "0.6.0", CancellationToken.None);
        await InstalledVersion.MigrateAsync(db, "0.9.2", CancellationToken.None);

        var installed = await InstalledVersion.GetAsync(db, "0.9.2", CancellationToken.None);

        Assert.Equal("0.6.0", installed.PreviousVersion);
        Assert.Equal("0.6.0", installed.ChangesSince);
    }

    [Fact]
    public async Task UnrecordedOldVersion_ShowsTheChangesSinceTheOldestItCanBe()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await InstalledVersion.RecordAsync(db, "0.6.1", existingDatabase: true, CancellationToken.None, unrecordedVersion: "0.4.0");

        var installed = await InstalledVersion.GetAsync(db, "0.6.1", CancellationToken.None);

        Assert.Null(installed.PreviousVersion);
        Assert.Equal("0.4.0", installed.ChangesSince);
    }

    [Fact]
    public async Task UpdateRecordedBy061_WithoutChangesSince_CountsAsFrom050()
    {
        // 0.6.0 and 0.6.1 stored an empty previous version and no ChangesSince.
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        db.ApplicationSettings.Add(new ApplicationSetting { Key = InstalledVersion.VersionKey, Value = "0.6.1" });
        db.ApplicationSettings.Add(new ApplicationSetting { Key = InstalledVersion.PreviousVersionKey, Value = "" });
        await db.SaveChangesAsync();

        var installed = await InstalledVersion.GetAsync(db, "0.6.1", CancellationToken.None);

        Assert.True(installed.Updated);
        Assert.Equal("0.5.0", installed.ChangesSince);
    }

    [Theory]
    [InlineData("20260927082105_AddPrintOptionsAndPrintServerQueue", "0.2.0")] // 0.2.0 – 0.3.0
    [InlineData("20260927145244_AddTemplateOwnerAndVisibility", "0.3.0")]      // between 0.3.0 and 0.4.0
    [InlineData("20260927172004_AddUploadedFileOwner", "0.4.0")]
    [InlineData("20260927200113_AddInstanceBinding", "0.5.0")]
    [InlineData("20261002135743_AddBulkPrintProfiles", "0.5.0")]              // 0.6.0 betas
    [InlineData("20260927195852_InitialCreate", "0.5.0")]                     // SQLite, 0.5.0
    [InlineData("20261002135747_AddBulkPrintProfiles", "0.5.0")]              // SQLite, 0.6.0 betas
    [InlineData("20260922170456_InitialCreate", "0.2.0")]
    public void UnrecordedVersion_IsEstimatedFromTheLastMigration(string lastMigration, string expected)
    {
        Assert.Equal(expected, InstalledVersion.EstimateUnrecordedVersion(lastMigration));
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
            Assert.Equal(new InstalledVersionResponse("0.7.0", true, "0.6.0", "0.6.0", updatedAt), installed);
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
