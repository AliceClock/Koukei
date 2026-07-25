using Koukei.Core.Tests.Infrastructure;
using Koukei.Data.Entities.Video.Movie;
using Koukei.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace Koukei.Core.Tests;

public sealed class PlaylistServiceTests
{
    [Fact]
    public async Task Playlist_lifecycle_keeps_counts_and_sort_order_consistent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;
        var first = new Movie { Name = "First" };
        var second = new Movie { Name = "Second" };
        var third = new Movie { Name = "Third" };
        context.Movies.AddRange(first, second, third);
        await context.SaveChangesAsync();

        var service = new PlaylistService(context);
        var playlist = await service.CreateAsync("  Favorites  ", "   ");
        var addResult = await service.AddItemsAsync(
            playlist.Id,
            [first.Id, second.Id, first.Id, Guid.Empty, Guid.NewGuid()]);

        Assert.Equal("Favorites", playlist.Name);
        Assert.Equal("FAVORITES", playlist.SortName);
        Assert.Null(playlist.Description);
        Assert.Equal(2, addResult.AddedCount);
        Assert.Equal(0, addResult.DuplicateCount);
        Assert.Equal(1, addResult.MissingCount);

        var duplicateResult = await service.AddItemsAsync(
            playlist.Id,
            [first.Id, third.Id, Guid.NewGuid()]);
        Assert.Equal(1, duplicateResult.AddedCount);
        Assert.Equal(1, duplicateResult.DuplicateCount);
        Assert.Equal(1, duplicateResult.MissingCount);

        var ordered = await context.PlaylistItems.AsNoTracking()
            .Where(item => item.PlaylistId == playlist.Id)
            .OrderBy(item => item.SortOrder)
            .ToListAsync();
        Assert.Equal([0, 1, 2], ordered.Select(item => item.SortOrder));

        Assert.True(await service.MoveItemAsync(ordered[2].Id, -10));
        ordered = await context.PlaylistItems.AsNoTracking()
            .Where(item => item.PlaylistId == playlist.Id)
            .OrderBy(item => item.SortOrder)
            .ToListAsync();
        Assert.Equal(third.Id, ordered[0].ItemId);
        Assert.Equal([0, 1, 2], ordered.Select(item => item.SortOrder));

        Assert.True(await service.RemoveItemAsync(ordered[1].Id));
        var remainingOrders = await context.PlaylistItems.AsNoTracking()
            .Where(item => item.PlaylistId == playlist.Id)
            .OrderBy(item => item.SortOrder)
            .Select(item => item.SortOrder)
            .ToListAsync();
        Assert.Equal([0, 1], remainingOrders);

        Assert.Equal(2, await service.ClearAsync(playlist.Id));
        Assert.Empty(await context.PlaylistItems.Where(item => item.PlaylistId == playlist.Id).ToListAsync());
        Assert.True(await service.DeleteAsync(playlist.Id));
        Assert.False(await service.DeleteAsync(playlist.Id));
    }
}
