using Koukei.Data.Entities;
using Koukei.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace Koukei.Data.Repositories;

public sealed class ItemRepository(KoukeiDbContext context) : IItemRepository
{
    public IQueryable<BaseItem> Query()
    {
        return context.Items;
    }

    public IQueryable<TItem> Query<TItem>() where TItem : BaseItem
    {
        return context.Set<TItem>();
    }

    public Task<BaseItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return context.Items.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public Task<TItem?> GetAsync<TItem>(Guid id, CancellationToken cancellationToken = default)
        where TItem : BaseItem
    {
        return context.Set<TItem>().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public Task<BaseItem?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return context.Items.FirstOrDefaultAsync(item => item.Path == path, cancellationToken);
    }

    public Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return context.Items.AnyAsync(item => item.Path == path, cancellationToken);
    }

    public Task<List<BaseItem>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default)
    {
        return context.Items
            .Where(item => item.ParentId == parentId)
            .OrderBy(item => item.ParentIndexNumber)
            .ThenBy(item => item.SortName ?? item.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<List<BaseItem>> GetByKindAsync(BaseItemKind kind, CancellationToken cancellationToken = default)
    {
        return context.Items
            .Where(item => EF.Property<BaseItemKind>(item, "ItemKind") == kind)
            .OrderBy(item => item.SortName ?? item.Name)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(BaseItem item, CancellationToken cancellationToken = default)
    {
        return context.Items.AddAsync(item, cancellationToken).AsTask();
    }

    public void Update(BaseItem item)
    {
        context.Items.Update(item);
    }

    public void Remove(BaseItem item)
    {
        context.Items.Remove(item);
    }
}
