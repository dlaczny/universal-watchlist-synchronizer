namespace Watchlist.Application;

/// <summary>
/// Preserves the exact pagination envelope supplied by one complete Trakt read.
/// </summary>
public sealed record TraktPagedResult<T>(
    int PageCount,
    int PageSize,
    IReadOnlyList<T> Items)
{
    private IReadOnlyList<T> _items = Snapshot(Items);

    public int PageCount { get; init; } = EnsurePageCount(PageCount);

    public int PageSize { get; init; } = EnsurePageSize(PageSize);

    public IReadOnlyList<T> Items
    {
        get => _items;
        init => _items = Snapshot(value);
    }

    private static int EnsurePageCount(int value)
    {
        return value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(PageCount));
    }

    private static int EnsurePageSize(int value)
    {
        return value is >= 1 and <= 100
            ? value
            : throw new ArgumentOutOfRangeException(nameof(PageSize));
    }

    private static IReadOnlyList<T> Snapshot(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
