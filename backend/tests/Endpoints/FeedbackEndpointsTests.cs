using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for Feedback endpoints.
/// POST /api/feedback requires authentication and tenant membership.
/// </summary>
[Collection("Database collection")]
public class FeedbackEndpointsTests
{
    private readonly HttpClient _client;
    private readonly HttpClient _unauthenticatedClient;

    public FeedbackEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        _unauthenticatedClient = databaseFixture.Factory.CreateClient();
    }

    #region POST /api/feedback

    [Fact]
    public async Task SubmitFeedback_NoAuth_Returns401()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "bug",
            Title = "Something is broken"
        };

        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/feedback", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_ValidBug_Returns201()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "bug",
            Title = $"Test Bug {Guid.NewGuid():N}"[..30],
            Description = "Steps to reproduce the issue",
            PageUrl = "/dashboard"
        };

        var response = await _client.PostAsJsonAsync("/api/feedback", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<FeedbackResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("bug", body.FeedbackType);
        Assert.Equal(request.Title, body.Title);
        Assert.Equal("new", body.Status);
    }

    [Fact]
    public async Task SubmitFeedback_ValidFeatureRequest_Returns201()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "feature",
            Title = $"Feature Req {Guid.NewGuid():N}"[..30]
        };

        var response = await _client.PostAsJsonAsync("/api/feedback", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_ValidQuestion_Returns201()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "question",
            Title = "How do I configure scheduling?"
        };

        var response = await _client.PostAsJsonAsync("/api/feedback", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_ValidOther_Returns201()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "other",
            Title = "General feedback about the app"
        };

        var response = await _client.PostAsJsonAsync("/api/feedback", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_InvalidType_Returns400()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "invalid_type",
            Title = "Some feedback"
        };

        var response = await _client.PostAsJsonAsync("/api/feedback", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_EmptyTitle_Returns400()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "bug",
            Title = ""
        };

        var response = await _client.PostAsJsonAsync("/api/feedback", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_TitleTooLong_Returns400()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "bug",
            Title = new string('A', 201) // DomainLimits.FeedbackTitleMaxLength is 200
        };

        var response = await _client.PostAsJsonAsync("/api/feedback", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_RouteExists_DoesNotReturn404()
    {
        var request = new CreateFeedbackRequest
        {
            FeedbackType = "bug",
            Title = "Route check"
        };

        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/feedback", request);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}
