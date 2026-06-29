namespace Api.Models;

public record Feedback
{
    public Guid Id { get; init; }
    public Guid? UserId { get; init; }
    public required string FeedbackType { get; init; } // 'bug', 'feature', 'question', 'other'
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? PageUrl { get; init; }
    public string? UserAgent { get; init; }
    public string Status { get; init; } = "new"; // 'new', 'reviewed', 'resolved', 'wont_fix'
    public string? AdminNotes { get; init; }
    public string? GithubIssueUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateFeedbackRequest
{
    public required string FeedbackType { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? PageUrl { get; init; }
}

public record FeedbackResponse
{
    public Guid Id { get; init; }
    public required string FeedbackType { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = "new";
    public DateTime CreatedAt { get; init; }
}

/// <summary>Row in the site-admin feedback list.</summary>
public record FeedbackSummary
{
    public Guid Id { get; init; }
    public required string FeedbackType { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public string? TenantName { get; init; }
    public string? SubmitterEmail { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>Full feedback item for the site-admin triage dialog.</summary>
public record FeedbackDetail
{
    public Guid Id { get; init; }
    public required string FeedbackType { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? PageUrl { get; init; }
    public string? UserAgent { get; init; }
    public required string Status { get; init; }
    public string? AdminNotes { get; init; }
    public string? GithubIssueUrl { get; init; }
    public string? TenantName { get; init; }
    public string? SubmitterEmail { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>Site-admin triage update — any subset; at least one field must be present.</summary>
public record UpdateFeedbackRequest
{
    public string? Status { get; init; }
    public string? AdminNotes { get; init; }
    public string? GithubIssueUrl { get; init; }
}

/// <summary>Controlled vocabulary for feedback status (matches the DB CHECK constraint).</summary>
public static class FeedbackStatuses
{
    public const string New = "new";
    public const string Reviewed = "reviewed";
    public const string Resolved = "resolved";
    public const string WontFix = "wont_fix";

    public static readonly HashSet<string> All = [New, Reviewed, Resolved, WontFix];
}

/// <summary>Controlled vocabulary for feedback type (matches the DB CHECK constraint).</summary>
public static class FeedbackTypes
{
    public static readonly HashSet<string> All = ["bug", "feature", "question", "other"];
}
