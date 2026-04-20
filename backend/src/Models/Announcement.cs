namespace Api.Models;

/// <summary>
/// Represents a platform-wide announcement created by a site administrator.
/// </summary>
public sealed record Announcement
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public bool IsImportant { get; init; }
    public int Revision { get; init; } = 1;
    public DateTime CreatedAt { get; init; }
    public Guid CreatedByUserId { get; init; }
    public DateTime UpdatedAt { get; init; }
    public Guid UpdatedByUserId { get; init; }
    public DateTime ExpiresAt { get; init; }
}

/// <summary>DTO returned to the admin UI.</summary>
public sealed record AnnouncementDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public bool IsImportant { get; init; }
    public int Revision { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedByEmail { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? UpdatedByEmail { get; init; }
    public DateTime ExpiresAt { get; init; }
    public bool IsExpired => ExpiresAt <= DateTime.UtcNow;
}

/// <summary>DTO returned to regular (non-admin) users.</summary>
public sealed record UserAnnouncementDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public bool IsImportant { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool IsRead { get; init; }
}

/// <summary>Request body for creating an announcement.</summary>
public sealed record CreateAnnouncementRequest
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public bool IsImportant { get; init; }
    /// <summary>Days until expiration. Default: 90.</summary>
    public int? RetentionDays { get; init; }
}

/// <summary>Request body for updating an announcement.</summary>
public sealed record UpdateAnnouncementRequest
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public bool IsImportant { get; init; }
    /// <summary>Optional new expiration. Omit to leave unchanged.</summary>
    public DateTime? ExpiresAt { get; init; }
}
