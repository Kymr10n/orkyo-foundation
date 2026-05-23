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
    public required SearchResultOpen Open { get; init; }
    public required SearchResultPermissions Permissions { get; init; }
}

public record SearchResultOpen
{
    public required string Route { get; init; }
    public Dictionary<string, string> Params { get; init; } = new();
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
