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

    /// <summary>
    /// Cap on a list branch that answers with the whole list instead of a page — the pickers
    /// that need every row. The single home for that number: a caller passes it to
    /// <see cref="PagedResult{T}.Capped"/> so a truncated answer still reports the real total.
    /// </summary>
    public const int MaxUnpagedItems = 1000;

    /// <summary>Return a sanitised copy with clamped values.</summary>
    public PageRequest Sanitize() => this with
    {
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize)
    };

    public int Offset => (Math.Max(1, Page) - 1) * Math.Clamp(PageSize, 1, MaxPageSize);

    /// <summary>
    /// Clamp a caller-supplied row limit for a list that is not offset-paged (search). Returns
    /// <paramref name="fallback"/> when nothing was requested, and never more than
    /// <paramref name="max"/> nor less than 1.
    /// </summary>
    public static int ClampLimit(int? requested, int fallback, int max)
        => Math.Clamp(requested ?? fallback, 1, max);

    /// <summary>
    /// Build a sanitised request from the optional <c>page</c>/<c>pageSize</c> query parameters
    /// an endpoint receives. Absent values fall back to page 1 and <see cref="DefaultPageSize"/>.
    /// </summary>
    public static PageRequest From(int? page, int? pageSize) => new PageRequest
    {
        Page = page ?? 1,
        PageSize = pageSize ?? DefaultPageSize,
    }.Sanitize();
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

    /// <summary>
    /// Envelope for the branch that answers with the whole list up to <paramref name="cap"/>
    /// rather than a page: page 1, page size <paramref name="cap"/>, and the real unpaged
    /// <paramref name="totalItems"/>, so <see cref="HasNextPage"/> is the truncation signal.
    /// </summary>
    /// <remarks>
    /// Deliberately bypasses <see cref="PageRequest.Sanitize"/>. The cap is the caller's, not a
    /// page size a client asked for, so <see cref="PageSize"/> on this one branch is above
    /// <see cref="PageRequest.MaxPageSize"/> — sanitising would silently shrink the cap to 100.
    /// </remarks>
    public static PagedResult<T> Capped(IReadOnlyList<T> items, int totalItems, int cap)
    {
        // A non-positive cap would make TotalPages 0 and HasNextPage false whatever totalItems
        // says, silently destroying the truncation signal this overload exists to carry.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        return new()
        {
            Items = items,
            Page = 1,
            PageSize = cap,
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
