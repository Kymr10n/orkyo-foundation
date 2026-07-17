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

    public async Task<List<SearchResult>> SearchAsync(string query, Guid? siteId, string[]? types, int limit = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var isShortQuery = normalizedQuery.Length < SearchConstants.MinQueryLengthForFullSearch;
        var sql = isShortQuery ? BuildTrigramOnlySql(types, siteId) : BuildCombinedSearchSql(types, siteId);
        var settings = await _settingsService.GetSettingsAsync(ct);

        await using var conn = _connectionFactory.CreateOrgConnection(_context);
        return await conn.QueryListAsync(sql, p =>
        {
            p.AddWithValue("@query", normalizedQuery);
            p.AddWithValue("@limit", limit);
            p.AddWithValue("@primaryThreshold", settings.Search_PrimarySimilarityThreshold);
            p.AddWithValue("@secondaryThreshold", settings.Search_SecondarySimilarityThreshold);
            if (siteId.HasValue) p.AddWithValue("@site_id", siteId.Value);
            if (types != null && types.Length > 0) p.AddWithValue("@types", types);
        }, MapResult, ct);
    }

    private static SearchResult MapResult(NpgsqlDataReader reader)
    {
        var entityType = reader.GetString(0);
        var entityId = reader.GetGuid(1);
        var resultSiteId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
        return new SearchResult
        {
            Type = entityType,
            Id = entityId,
            Title = reader.GetString(2),
            Subtitle = reader.IsDBNull(3) ? null : reader.GetString(3),
            SiteId = resultSiteId,
            Score = reader.GetDouble(5),
            UpdatedAt = reader.GetDateTime(6),
            Open = GetOpenRoute(entityType, entityId, resultSiteId),
            Permissions = new SearchResultPermissions { CanRead = true, CanEdit = false }
        };
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
            SearchEntityTypes.Space => new SearchResultOpen { Route = "/spaces", Params = new Dictionary<string, string> { ["resourceId"] = entityId.ToString(), ["mode"] = "edit" } },
            SearchEntityTypes.Request => new SearchResultOpen { Route = "/requests", Params = new Dictionary<string, string> { ["requestId"] = entityId.ToString(), ["mode"] = "edit" } },
            SearchEntityTypes.Group => new SearchResultOpen { Route = "/settings/groups", Params = new Dictionary<string, string> { ["groupId"] = entityId.ToString(), ["mode"] = "edit" } },
            SearchEntityTypes.Site => new SearchResultOpen { Route = "/settings/sites", Params = new Dictionary<string, string> { ["siteId"] = entityId.ToString(), ["mode"] = "edit" } },
            SearchEntityTypes.Template => new SearchResultOpen { Route = "/settings/templates", Params = new Dictionary<string, string> { ["templateId"] = entityId.ToString(), ["mode"] = "edit" } },
            SearchEntityTypes.Criterion => new SearchResultOpen { Route = "/settings/criteria", Params = new Dictionary<string, string> { ["criterionId"] = entityId.ToString(), ["mode"] = "edit" } },
            SearchEntityTypes.Person => new SearchResultOpen { Route = "/people/list", Params = new Dictionary<string, string> { ["personId"] = entityId.ToString() } },
            _ => new SearchResultOpen { Route = "/", Params = new Dictionary<string, string>() }
        };
}
