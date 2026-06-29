using Api.Models;
using Api.Security;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IFeedbackRepository
{
    Task<FeedbackResponse> CreateAsync(CreateFeedbackRequest request, Guid? userId, string? userAgent, CancellationToken ct = default);
    Task<(IReadOnlyList<FeedbackSummary> Items, int Total)> ListAsync(string? status, string? type, int limit, int offset, CancellationToken ct = default);
    Task<FeedbackDetail?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FeedbackDetail?> UpdateAsync(Guid id, UpdateFeedbackRequest request, CancellationToken ct = default);
}

/// <summary>
/// Control-plane persistence for platform feedback (relocated from per-tenant DBs so a site-admin can
/// triage every submission in one place — table created by migration 1170). Records the originating
/// tenant via <c>tenant_id</c>. Raw SQL via the foundation Npgsql helpers; no EF.
/// </summary>
public class FeedbackRepository(ICurrentTenant currentTenant, IDbConnectionFactory connectionFactory)
    : IFeedbackRepository
{
    // Detail column order shared by GetByIdAsync + UpdateAsync (matches MapDetail).
    private const string DetailColumns = @"
        f.id, f.feedback_type, f.title, f.description, f.page_url, f.user_agent,
        f.status, f.admin_notes, f.github_issue_url,
        t.display_name AS tenant_name, u.email AS submitter_email, f.created_at, f.updated_at";

    private const string DetailJoins = @"
        LEFT JOIN tenants t ON t.id = f.tenant_id
        LEFT JOIN users u ON u.id = f.user_id";

    public async Task<FeedbackResponse> CreateAsync(CreateFeedbackRequest request, Guid? userId, string? userAgent, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();

        return (await conn.QuerySingleOrDefaultAsync(@"
            INSERT INTO feedback (tenant_id, user_id, feedback_type, title, description, page_url, user_agent)
            VALUES (@tenant_id, @user_id, @feedback_type, @title, @description, @page_url, @user_agent)
            RETURNING id, feedback_type, title, description, status, created_at",
            p =>
            {
                p.AddWithValue("tenant_id", currentTenant.RequireTenantId());
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

    public async Task<(IReadOnlyList<FeedbackSummary> Items, int Total)> ListAsync(
        string? status, string? type, int limit, int offset, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) filters.Add("f.status = @status");
        if (!string.IsNullOrWhiteSpace(type)) filters.Add("f.feedback_type = @type");
        var where = filters.Count > 0 ? "WHERE " + string.Join(" AND ", filters) : "";

        void BindFilters(NpgsqlParameterCollection p)
        {
            if (!string.IsNullOrWhiteSpace(status)) p.AddWithValue("status", status);
            if (!string.IsNullOrWhiteSpace(type)) p.AddWithValue("type", type);
        }

        var total = await conn.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM feedback f {where}", BindFilters, ct);

        var items = await conn.QueryListAsync(
            $@"SELECT f.id, f.feedback_type, f.title, f.status,
                      t.display_name AS tenant_name, u.email AS submitter_email, f.created_at
               FROM feedback f {DetailJoins}
               {where}
               ORDER BY f.created_at DESC
               LIMIT @limit OFFSET @offset",
            p => { BindFilters(p); p.AddWithValue("limit", limit); p.AddWithValue("offset", offset); },
            r => new FeedbackSummary
            {
                Id = r.GetGuid(0),
                FeedbackType = r.GetString(1),
                Title = r.GetString(2),
                Status = r.GetString(3),
                TenantName = r.IsDBNull(4) ? null : r.GetString(4),
                SubmitterEmail = r.IsDBNull(5) ? null : r.GetString(5),
                CreatedAt = r.GetDateTime(6),
            }, ct);

        return (items, (int)total);
    }

    public async Task<FeedbackDetail?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {DetailColumns} FROM feedback f {DetailJoins} WHERE f.id = @id",
            p => p.AddWithValue("id", id), MapDetail, ct);
    }

    public async Task<FeedbackDetail?> UpdateAsync(Guid id, UpdateFeedbackRequest request, CancellationToken ct = default)
    {
        var builder = new UpdateBuilder()
            .SetIfNotNull("status", request.Status)
            .SetIfNotNull("admin_notes", request.AdminNotes)
            .SetIfNotNull("github_issue_url", request.GithubIssueUrl)
            .SetExpression("updated_at = NOW()");

        await using var conn = connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(
            $@"WITH updated AS (
                   UPDATE feedback SET {builder.SetClause} WHERE id = @id RETURNING *
               )
               SELECT {DetailColumns} FROM updated f {DetailJoins}",
            p => { builder.Apply(p); p.AddWithValue("id", id); }, MapDetail, ct);
    }

    private static FeedbackDetail MapDetail(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        FeedbackType = r.GetString(1),
        Title = r.GetString(2),
        Description = r.IsDBNull(3) ? null : r.GetString(3),
        PageUrl = r.IsDBNull(4) ? null : r.GetString(4),
        UserAgent = r.IsDBNull(5) ? null : r.GetString(5),
        Status = r.GetString(6),
        AdminNotes = r.IsDBNull(7) ? null : r.GetString(7),
        GithubIssueUrl = r.IsDBNull(8) ? null : r.GetString(8),
        TenantName = r.IsDBNull(9) ? null : r.GetString(9),
        SubmitterEmail = r.IsDBNull(10) ? null : r.GetString(10),
        CreatedAt = r.GetDateTime(11),
        UpdatedAt = r.GetDateTime(12),
    };
}
