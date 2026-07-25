using Koukei.Core.Tests.Infrastructure;
using Koukei.Data.Entities.Video.Movie;
using Koukei.Data.Services;

namespace Koukei.Core.Tests;

public sealed class UserMediaStateServiceTests
{
    [Fact]
    public async Task User_state_upserts_favorite_rating_position_and_play_history()
    {
        await using var database = await TestDatabase.CreateAsync();
        var item = new Movie { Name = "Movie" };
        database.Context.Movies.Add(item);
        await database.Context.SaveChangesAsync();
        var service = new UserMediaStateService(database.Context);

        Assert.True((await service.SetFavoriteAsync(item.Id, true)).IsFavorite);
        Assert.Equal(5, (await service.SetUserRatingAsync(item.Id, 5)).UserRating);
        Assert.Null((await service.SetUserRatingAsync(item.Id, 0)).UserRating);
        Assert.Equal(
            0L,
            (await service.SetPlaybackPositionAsync(item.Id, TimeSpan.FromSeconds(-5)))
                .PlaybackPositionTicks);

        var playedAt = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        await service.RecordPlayedAsync(item.Id, TimeSpan.FromSeconds(42), playedAt);
        var state = await service.RecordPlayedAsync(item.Id, playedAt: playedAt.AddMinutes(1));

        Assert.Equal(2, state.PlayCount);
        Assert.Equal(playedAt.AddMinutes(1), state.LastPlayedAt);
        Assert.Equal(TimeSpan.FromSeconds(42).Ticks, state.PlaybackPositionTicks);
        Assert.Equal(item.Id, Assert.Single(await service.GetRecentlyOpenedAsync(10)).ItemId);
        Assert.True(await service.DeleteAsync(item.Id));
        Assert.False(await service.DeleteAsync(item.Id));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task User_state_rejects_out_of_range_ratings(int rating)
    {
        await using var database = await TestDatabase.CreateAsync();
        var item = new Movie { Name = "Movie" };
        database.Context.Movies.Add(item);
        await database.Context.SaveChangesAsync();
        var service = new UserMediaStateService(database.Context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SetUserRatingAsync(item.Id, rating));
    }

    [Fact]
    public async Task User_state_rejects_unknown_items()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new UserMediaStateService(database.Context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetFavoriteAsync(Guid.NewGuid(), true));
    }
}
