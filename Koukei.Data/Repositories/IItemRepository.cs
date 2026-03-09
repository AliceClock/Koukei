using Koukei.Data.Entities;
using Koukei.Data.Enums;

namespace Koukei.Data.Repositories;

public interface IItemRepository
{
    IQueryable<BaseItem> Query();

    IQueryable<TItem> Query<TItem>() where TItem : BaseItem;

    Task<BaseItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TItem?> GetAsync<TItem>(Guid id, CancellationToken cancellationToken = default) where TItem : BaseItem;

    Task<BaseItem?> GetByPathAsync(string path, CancellationToken cancellationToken = default);

    Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<List<BaseItem>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);

    Task<List<BaseItem>> GetByKindAsync(BaseItemKind kind, CancellationToken cancellationToken = default);

    Task AddAsync(BaseItem item, CancellationToken cancellationToken = default);

    void Update(BaseItem item);

    void Remove(BaseItem item);
}
