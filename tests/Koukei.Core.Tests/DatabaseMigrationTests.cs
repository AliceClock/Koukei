using Koukei.Core.Tests.Infrastructure;
using Koukei.Data.Entities.Video.Movie;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Koukei.Core.Tests;

public sealed class DatabaseMigrationTests
{
    private const string PreviousMigration = "20260722120000_OptimizeMediaImport";

    [Fact]
    public async Task Empty_database_migrates_to_valid_schema_and_persists_linked_media_path()
    {
        using var temp = new TempDirectory();
        var databasePath = temp.GetPath("koukei.db");

        await KoukeiDatabase.EnsureReadyAsync(databasePath);
        var verification = await KoukeiSchemaVerifier.VerifySqliteAsync(databasePath);

        Assert.True(
            verification.IsValid,
            $"Missing tables: {string.Join(", ", verification.MissingTables)}; " +
            $"missing indexes: {string.Join(", ", verification.MissingIndexes)}; " +
            $"missing unique indexes: {string.Join(", ", verification.MissingUniqueIndexes)}");

        var id = Guid.NewGuid();
        await using (var context = KoukeiDatabase.CreateSqliteContext(databasePath))
        {
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            context.Movies.Add(new Movie
            {
                Id = id,
                Name = "Linked movie",
                LinkedFilePath = @"C:\media\movie.en.srt"
            });
            await context.SaveChangesAsync();
        }

        await using (var reloadedContext = KoukeiDatabase.CreateSqliteContext(databasePath))
        {
            var reloaded = await reloadedContext.Movies.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal(@"C:\media\movie.en.srt", reloaded.LinkedFilePath);
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Existing_database_upgrades_from_previous_migration_without_losing_data()
    {
        using var temp = new TempDirectory();
        var databasePath = temp.GetPath("upgrade.db");

        try
        {
            await using (var context = KoukeiDatabase.CreateSqliteContext(databasePath))
            {
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration);
                await context.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO AppSettings ("Key", "Value", "DateModified")
                    VALUES ('upgrade-marker', 'preserved', 0)
                    """);
                await migrator.MigrateAsync();
            }

            await using var upgradedContext = KoukeiDatabase.CreateSqliteContext(databasePath);
            var marker = await upgradedContext.AppSettings
                .AsNoTracking()
                .SingleAsync(setting => setting.Key == "upgrade-marker");

            Assert.Equal("preserved", marker.Value);
            Assert.Empty(await upgradedContext.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
