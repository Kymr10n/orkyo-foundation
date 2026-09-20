using Api.Helpers;
using Api.Models;
using Api.Security;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IFeedbackRepository
{
    Task<FeedbackResponse> CreateAsync(CreateFeedbackRequest request, Guid? userId, string? userAgent, CancellationToken ct = default);
    Task<PagedResult<FeedbackSummary>> ListAsync(string? status, string? type, PageRequest page, CancellationToken ct = default);
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
                Id = r.GetGuid("id"),
                FeedbackType = r.GetString("feedback_type"),
                Title = r.GetString("title"),
                Description = r.GetNullableString("description"),
                Status = r.GetString("status"),
                CreatedAt = r.GetDateTime("created_at")
            }, ct))!;
    }

    public async Task<PagedResult<FeedbackSummary>> ListAsync(
        string? status, string? type, PageRequest page, CancellationToken ct = default)
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

        return await conn.QueryPagedAsync(
            page,
            $"SELECT COUNT(*) FROM feedback f {where}",
            $@"SELECT f.id, f.feedback_type, f.title, f.status,
                      t.display_name AS tenant_name, u.email AS submitter_email, f.created_at
               FROM feedback f {DetailJoins}
               {where}
               ORDER BY f.created_at DESC
               LIMIT @limit OFFSET @offset",
            BindFilters,
            r => new FeedbackSummary
            {
                Id = r.GetGuid("id"),
                FeedbackType = r.GetString("feedback_type"),
                Title = r.GetString("title"),
                Status = r.GetString("status"),
                TenantName = r.GetNullableString("tenant_name"),
                SubmitterEmail = r.GetNullableString("submitter_email"),
                CreatedAt = r.GetDateTime("created_at"),
            }, ct);
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
        Id = r.GetGuid("id"),
        FeedbackType = r.GetString("feedback_type"),
        Title = r.GetString("title"),
        Description = r.GetNullableString("description"),
        PageUrl = r.GetNullableString("page_url"),
        UserAgent = r.GetNullableString("user_agent"),
        Status = r.GetString("status"),
        AdminNotes = r.GetNullableString("admin_notes"),
        GithubIssueUrl = r.GetNullableString("github_issue_url"),
        TenantName = r.GetNullableString("tenant_name"),
        SubmitterEmail = r.GetNullableString("submitter_email"),
        CreatedAt = r.GetDateTime("created_at"),
        UpdatedAt = r.GetDateTime("updated_at"),
    };
}
