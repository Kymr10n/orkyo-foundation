using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IFeedbackRepository
{
    Task<FeedbackResponse> CreateAsync(CreateFeedbackRequest request, Guid? userId, string? userAgent);
}

public class FeedbackRepository : IFeedbackRepository
{
    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public FeedbackRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<FeedbackResponse> CreateAsync(CreateFeedbackRequest request, Guid? userId, string? userAgent)
    {
        await using var db = _connectionFactory.CreateOrgConnection(_orgContext);
        await db.OpenAsync();

        var cmd = new NpgsqlCommand(@"
            INSERT INTO feedback (user_id, feedback_type, title, description, page_url, user_agent)
            VALUES (@user_id, @feedback_type, @title, @description, @page_url, @user_agent)
            RETURNING id, feedback_type, title, description, status, created_at
        ", db);

        cmd.Parameters.AddWithValue("user_id", userId.HasValue ? userId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("feedback_type", request.FeedbackType);
        cmd.Parameters.AddWithValue("title", request.Title);
        cmd.Parameters.AddWithValue("description", request.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("page_url", request.PageUrl ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("user_agent", userAgent ?? (object)DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new FeedbackResponse
        {
            Id = reader.GetGuid(0),
            FeedbackType = reader.GetString(1),
            Title = reader.GetString(2),
            Description = reader.IsDBNull(3) ? null : reader.GetString(3),
            Status = reader.GetString(4),
            CreatedAt = reader.GetDateTime(5)
        };
    }
}
