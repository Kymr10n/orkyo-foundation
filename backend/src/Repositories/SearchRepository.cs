using Api.Constants;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SearchRepository : ISearchRepository
{
    private readonly OrgContext _context;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantSettingsService _settingsService;

    public SearchRepository(OrgContext context, IDbConnectionFactory connectionFactory, ITenantSettingsService settingsService)
    {
        _context = context;
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, Guid? siteId, string[]? types, int limit = 20)
    {
        var results = new List<SearchResult>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        await using var conn = _connectionFactory.CreateOrgConnection(_context);
        await conn.OpenAsync();

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var isShortQuery = normalizedQuery.Length < SearchConstants.MinQueryLengthForFullSearch;
        var sql = isShortQuery ? BuildTrigramOnlySql(types, siteId) : BuildCombinedSearchSql(types, siteId);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@query", normalizedQuery);
        cmd.Parameters.AddWithValue("@limit", limit);

        var settings = await _settingsService.GetSettingsAsync();
        cmd.Parameters.AddWithValue("@primaryThreshold", settings.Search_PrimarySimilarityThreshold);
        cmd.Parameters.AddWithValue("@secondaryThreshold", settings.Search_SecondarySimilarityThreshold);

        if (siteId.HasValue) cmd.Parameters.AddWithValue("@site_id", siteId.Value);
        if (types != null && types.Length > 0) cmd.Parameters.AddWithValue("@types", types);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entityType = reader.GetString(0);
            var entityId = reader.GetGuid(1);
            results.Add(new SearchResult
            {
                Type = entityType,
                Id = entityId,
                Title = reader.GetString(2),
                Subtitle = reader.IsDBNull(3) ? null : reader.GetString(3),
                SiteId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                Score = reader.GetDouble(5),
                UpdatedAt = reader.GetDateTime(6),
                Open = GetOpenRoute(entityType, entityId, reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4)),
                Permissions = new SearchResultPermissions { CanRead = true, CanEdit = false }
            });
        }

        return results;
    }

    private static string BuildCombinedSearchSql(string[]? types, Guid? siteId)
    {
        var where = new List<string>();
        if (siteId.HasValue) where.Add("(site_id IS NULL OR site_id = @site_id)");
        if (types != null && types.Length > 0) where.Add("entity_type = ANY(@types)");
        var whereStr = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        return $@"
            WITH ranked AS (
                SELECT entity_type, entity_id, title, subtitle, site_id, updated_at,
                    COALESCE(ts_rank(fts, plainto_tsquery('simple', @query)), 0) AS fts_score,
                    GREATEST(similarity(title, @query), similarity(COALESCE(keywords, ''), @query) * 0.8) AS trgm_score
                FROM search_documents {whereStr}
            )
            SELECT entity_type, entity_id, title, subtitle, site_id,
                (fts_score * 10 + trgm_score)::float8 AS score, updated_at
            FROM ranked
            WHERE fts_score > 0 OR trgm_score > @primaryThreshold
            ORDER BY score DESC, updated_at DESC LIMIT @limit";
    }

    private static string BuildTrigramOnlySql(string[]? types, Guid? siteId)
    {
        var where = new List<string>
        {
            "(lower(title) LIKE @query || '%' OR similarity(title, @query) > @secondaryThreshold OR similarity(COALESCE(keywords, ''), @query) > @secondaryThreshold)"
        };
        if (siteId.HasValue) where.Add("(site_id IS NULL OR site_id = @site_id)");
        if (types != null && types.Length > 0) where.Add("entity_type = ANY(@types)");
        var whereStr = "WHERE " + string.Join(" AND ", where);

        return $@"
            SELECT entity_type, entity_id, title, subtitle, site_id,
                GREATEST(
                    CASE WHEN lower(title) LIKE @query || '%' THEN 1.0 ELSE 0.0 END,
                    similarity(title, @query),
                    similarity(COALESCE(keywords, ''), @query) * 0.8
                )::float8 AS score, updated_at
            FROM search_documents {whereStr}
            ORDER BY score DESC, updated_at DESC LIMIT @limit";
    }

    private static SearchResultOpen GetOpenRoute(string entityType, Guid entityId, Guid? siteId) =>
        entityType switch
        {
            "space" => new SearchResultOpen { Route = "/spaces", Params = new Dictionary<string, string> { ["spaceId"] = entityId.ToString(), ["mode"] = "edit" } },
            "request" => new SearchResultOpen { Route = "/requests", Params = new Dictionary<string, string> { ["requestId"] = entityId.ToString(), ["mode"] = "edit" } },
            "group" => new SearchResultOpen { Route = "/settings/groups", Params = new Dictionary<string, string> { ["groupId"] = entityId.ToString(), ["mode"] = "edit" } },
            "site" => new SearchResultOpen { Route = "/settings/sites", Params = new Dictionary<string, string> { ["siteId"] = entityId.ToString(), ["mode"] = "edit" } },
            "template" => new SearchResultOpen { Route = "/settings/templates", Params = new Dictionary<string, string> { ["templateId"] = entityId.ToString(), ["mode"] = "edit" } },
            "criterion" => new SearchResultOpen { Route = "/settings/criteria", Params = new Dictionary<string, string> { ["criterionId"] = entityId.ToString(), ["mode"] = "edit" } },
            _ => new SearchResultOpen { Route = "/", Params = new Dictionary<string, string>() }
        };
}
