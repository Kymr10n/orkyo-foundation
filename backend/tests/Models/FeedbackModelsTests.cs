using Api.Models;
using Xunit;

namespace Api.Tests.Models;

public class FeedbackModelsTests
{
    [Fact]
    public void Feedback_DefaultStatus_IsNew()
    {
        var feedback = new Feedback
        {
            Id = Guid.NewGuid(),
            FeedbackType = "bug",
            Title = "Broken button"
        };

        Assert.Equal("new", feedback.Status);
        Assert.Equal("bug", feedback.FeedbackType);
        Assert.Equal("Broken button", feedback.Title);
        Assert.Null(feedback.Description);
    }

    [Fact]
    public void Feedback_CanSetOptionalFields()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var feedback = new Feedback
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FeedbackType = "feature",
            Title = "Add export",
            Description = "Need CSV export",
            PageUrl = "/dashboard",
            UserAgent = "Mozilla",
            Status = "reviewed",
            AdminNotes = "triaged",
            GithubIssueUrl = "https://github.com/example/repo/issues/1",
            CreatedAt = now,
            UpdatedAt = now
        };

        Assert.Equal(userId, feedback.UserId);
        Assert.Equal("reviewed", feedback.Status);
        Assert.Equal("triaged", feedback.AdminNotes);
        Assert.Equal("https://github.com/example/repo/issues/1", feedback.GithubIssueUrl);
    }

    [Fact]
    public void CreateFeedbackRequest_StoresProvidedValues()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "question",
            Title = "How to configure",
            Description = "Need docs",
            PageUrl = "/settings"
        };

        Assert.Equal("question", request.FeedbackType);
        Assert.Equal("How to configure", request.Title);
        Assert.Equal("Need docs", request.Description);
        Assert.Equal("/settings", request.PageUrl);
    }

    [Fact]
    public void FeedbackResponse_DefaultStatus_IsNew()
    {
        var response = new FeedbackResponse
        {
            Id = Guid.NewGuid(),
            FeedbackType = "other",
            Title = "FYI"
        };

        Assert.Equal("new", response.Status);
    }
}
