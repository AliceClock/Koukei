using Microsoft.EntityFrameworkCore;

namespace Koukei.Data.Repositories;

public sealed class KoukeiUnitOfWork(KoukeiDbContext context) : IKoukeiUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = [];
    private IItemRepository? _items;

    public KoukeiDbContext Context => context;

    public IItemRepository Items => _items ??= new ItemRepository(context);

    public IRepository<TEntity> Repository<TEntity>() where TEntity : class
    {
        var entityType = typeof(TEntity);

        if (_repositories.TryGetValue(entityType, out var repository))
        {
            return (IRepository<TEntity>)repository;
        }

        var created = new EfRepository<TEntity>(context);
        _repositories.Add(entityType, created);
        return created;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return context.SaveChangesAsync(cancellationToken);
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await operation(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
