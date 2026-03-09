namespace Koukei.Data.Repositories;

public interface IKoukeiUnitOfWork
{
    KoukeiDbContext Context { get; }

    IItemRepository Items { get; }

    IRepository<TEntity> Repository<TEntity>() where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
