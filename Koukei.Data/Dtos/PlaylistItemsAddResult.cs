namespace Koukei.Data.Dtos;

public sealed class PlaylistItemsAddResult
{
    public int AddedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int MissingCount { get; init; }
}
