namespace Api.Models;

/// <summary>
/// Represents a search result from the global search.
/// </summary>
public record SearchResult
{
    public required string Type { get; init; }
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public Guid? SiteId { get; init; }
    public double Score { get; init; }
    public DateTime UpdatedAt { get; init; }
    public required SearchResultPermissions Permissions { get; init; }
    /// <summary>
    /// The resource type key for resource results ("space", "person", "tool", or a
    /// tenant-defined one), and for group results the type the group holds. Null otherwise.
    /// The client routes and labels from this — the backend no longer computes a route,
    /// because it does not own the frontend's URL structure and its guesses had drifted.
    /// </summary>
    public string? ResourceTypeKey { get; init; }
}


public record SearchResultPermissions
{
    public bool CanRead { get; init; } = true;
    public bool CanEdit { get; init; }
}

public record SearchResponse
{
    public required string Query { get; init; }
    public required List<SearchResult> Results { get; init; }
}
