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
