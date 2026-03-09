using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace Koukei.Data;

public static class KoukeiDatabase
{
    public const string DefaultDatabaseFileName = "koukei.db";

    public static DbContextOptions<KoukeiDbContext> CreateSqliteOptions(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        EnsureDatabaseDirectory(databasePath);

        return new DbContextOptionsBuilder<KoukeiDbContext>()
            .UseSqlite(CreateSqliteConnectionString(databasePath))
            .Options;
    }

    public static KoukeiDbContext CreateSqliteContext(string databasePath)
    {
        return new KoukeiDbContext(CreateSqliteOptions(databasePath));
    }

    public static async Task EnsureCreatedAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await using var context = CreateSqliteContext(databasePath);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await ApplySqlitePragmasAsync(context, new KoukeiDataOptions { DatabasePath = databasePath }, cancellationToken);
    }

    public static async Task MigrateAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await using var context = CreateSqliteContext(databasePath);
        await context.Database.MigrateAsync(cancellationToken);
        await ApplySqlitePragmasAsync(context, new KoukeiDataOptions { DatabasePath = databasePath }, cancellationToken);
    }

    public static async Task EnsureReadyAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await using var context = CreateSqliteContext(databasePath);
        var migrations = context.Database.GetMigrations();

        if (migrations.Any())
        {
            await context.Database.MigrateAsync(cancellationToken);
            await ApplySqlitePragmasAsync(context, new KoukeiDataOptions { DatabasePath = databasePath }, cancellationToken);
            return;
        }

        await context.Database.EnsureCreatedAsync(cancellationToken);
        await ApplySqlitePragmasAsync(context, new KoukeiDataOptions { DatabasePath = databasePath }, cancellationToken);
    }

    public static string CreateSqliteConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        EnsureDatabaseDirectory(databasePath);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            DefaultTimeout = 30
        };

        return builder.ConnectionString;
    }

    public static void EnsureDatabaseDirectory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (databasePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public static async Task VacuumAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await using var context = CreateSqliteContext(databasePath);
        await context.Database.ExecuteSqlRawAsync("VACUUM;", cancellationToken);
    }

    public static async Task AnalyzeAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await using var context = CreateSqliteContext(databasePath);
        await context.Database.ExecuteSqlRawAsync("ANALYZE;", cancellationToken);
    }

    public static async Task CheckpointWalAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(CreateSqliteConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
    }

    public static async Task BackupSqliteAsync(
        string databasePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var sourcePath = Path.GetFullPath(databasePath);
        var targetPath = Path.GetFullPath(destinationPath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The source SQLite database does not exist.", sourcePath);
        }

        EnsureDatabaseDirectory(targetPath);

        if (File.Exists(targetPath))
        {
            if (!overwrite)
            {
                throw new IOException($"The backup destination '{targetPath}' already exists.");
            }

            File.Delete(targetPath);
        }

        await using var source = new SqliteConnection(CreateSqliteConnectionString(sourcePath));
        await using var destination = new SqliteConnection(CreateSqliteConnectionString(targetPath));
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        source.BackupDatabase(destination);
    }

    internal static async Task ApplySqlitePragmasAsync(
        KoukeiDbContext context,
        KoukeiDataOptions options,
        CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);

        if (options.EnableWriteAheadLog)
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
        }
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
