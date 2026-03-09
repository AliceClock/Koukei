namespace Koukei.Data.Dtos;

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public int TotalCount { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; }
}
