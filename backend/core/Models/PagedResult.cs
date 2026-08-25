namespace Api.Models;

/// <summary>
/// Offset-based pagination request parameters.
/// Parsed from query string: ?page=1&amp;pageSize=50
/// </summary>
public record PageRequest
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page (max 100).</summary>
    public int PageSize { get; init; } = DefaultPageSize;

    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>Return a sanitised copy with clamped values.</summary>
    public PageRequest Sanitize() => this with
    {
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize)
    };

    public int Offset => (Math.Max(1, Page) - 1) * Math.Clamp(PageSize, 1, MaxPageSize);
}

/// <summary>
/// Envelope for paginated list responses.
/// Backward-compatible: clients that ignore the metadata still get an <c>items</c> array.
/// </summary>
public record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int totalItems, PageRequest request)
    {
        var sanitized = request.Sanitize();
        return new PagedResult<T>
        {
            Items = items,
            Page = sanitized.Page,
            PageSize = sanitized.PageSize,
            TotalItems = totalItems
        };
    }
}

/// <summary>
/// How a request search orders its results. A closed set rather than a free-text column,
/// so an ordering can never be composed from caller input.
/// </summary>
public enum RequestSort
{
    /// <summary>Tree order — parents first, then each level's own sort order.</summary>
    Default,
    /// <summary>Longest scheduled window first; requests with no window come last.</summary>
    LongestDuration,
    /// <summary>Earliest start first; requests with no start come last.</summary>
    EarliestStart,
    Name,
}
