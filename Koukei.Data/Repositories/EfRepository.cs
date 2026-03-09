namespace Koukei.Data.Repositories;

public class EfRepository<TEntity>(KoukeiDbContext context) : IRepository<TEntity>
    where TEntity : class
{
    public IQueryable<TEntity> Query()
    {
        return context.Set<TEntity>();
    }

    public ValueTask<TEntity?> FindAsync(object[] keyValues, CancellationToken cancellationToken = default)
    {
        return context.Set<TEntity>().FindAsync(keyValues, cancellationToken);
    }

    public Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return context.Set<TEntity>().AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(TEntity entity)
    {
        context.Set<TEntity>().Update(entity);
    }

    public void Remove(TEntity entity)
    {
        context.Set<TEntity>().Remove(entity);
    }
}
