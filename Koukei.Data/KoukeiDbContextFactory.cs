using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Koukei.Data;

public sealed class KoukeiDbContextFactory : IDesignTimeDbContextFactory<KoukeiDbContext>
{
    public KoukeiDbContext CreateDbContext(string[] args)
    {
        var databasePath = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0]
            : Path.Combine(Environment.CurrentDirectory, KoukeiDatabase.DefaultDatabaseFileName);

        var options = new DbContextOptionsBuilder<KoukeiDbContext>()
            .UseSqlite(KoukeiDatabase.CreateSqliteConnectionString(databasePath))
            .Options;

        return new KoukeiDbContext(options);
    }
}
