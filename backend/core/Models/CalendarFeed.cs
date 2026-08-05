namespace Api.Models;

/// <summary>A calendar subscription, as stored. Never carries the token itself.</summary>
public record CalendarFeedTokenInfo
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public string? Label { get; init; }
    /// <summary>Restricts the feed to one site; null means every site the user can see.</summary>
    public Guid? SiteId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public DateTime? RevokedAt { get; init; }
}

public record CreateCalendarFeedRequest
{
    /// <summary>What the user calls this subscription, e.g. "Outlook, laptop".</summary>
    public string? Label { get; init; }
    public Guid? SiteId { get; init; }
}

/// <summary>
/// The create response — the only time the token is ever readable. It is not
/// stored in plaintext, so a user who loses the URL creates a new subscription
/// rather than recovering this one.
/// </summary>
public record CalendarFeedCreatedResponse
{
    public required Guid Id { get; init; }
    public required string FeedUrl { get; init; }
    public string? Label { get; init; }
    public Guid? SiteId { get; init; }
}
