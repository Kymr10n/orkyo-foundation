using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IFeedbackRepository
{
    Task<FeedbackResponse> CreateAsync(CreateFeedbackRequest request, Guid? userId, string? userAgent, CancellationToken ct = default);
}

public class FeedbackRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IFeedbackRepository
{
    public async Task<FeedbackResponse> CreateAsync(CreateFeedbackRequest request, Guid? userId, string? userAgent, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        return (await db.QuerySingleOrDefaultAsync(@"
            INSERT INTO feedback (user_id, feedback_type, title, description, page_url, user_agent)
            VALUES (@user_id, @feedback_type, @title, @description, @page_url, @user_agent)
            RETURNING id, feedback_type, title, description, status, created_at",
            p =>
            {
                p.AddNullable("user_id", userId);
                p.AddWithValue("feedback_type", request.FeedbackType);
                p.AddWithValue("title", request.Title);
                p.AddNullable("description", request.Description);
                p.AddNullable("page_url", request.PageUrl);
                p.AddNullable("user_agent", userAgent);
            },
            r => new FeedbackResponse
            {
                Id = r.GetGuid(0),
                FeedbackType = r.GetString(1),
                Title = r.GetString(2),
                Description = r.IsDBNull(3) ? null : r.GetString(3),
                Status = r.GetString(4),
                CreatedAt = r.GetDateTime(5)
            }, ct))!;
    }
}
