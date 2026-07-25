using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Koukei.Core.Tests.Infrastructure;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestDatabase(SqliteConnection connection, KoukeiDbContext context)
    {
        _connection = connection;
        Context = context;
    }

    public KoukeiDbContext Context { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<KoukeiDbContext>()
            .UseSqlite(connection)
            .EnableDetailedErrors()
            .Options;
        var context = new KoukeiDbContext(options);

        try
        {
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }
        catch
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
