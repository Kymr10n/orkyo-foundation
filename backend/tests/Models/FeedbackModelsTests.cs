using Api.Models;

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
