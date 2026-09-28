using Api.Constants;
using Api.Helpers;
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
        await conn.OpenAsync(ct);
        // The trigram arms filter with `%`, the operator the GIN indexes of migration 1280 serve.
        // It compares against pg_trgm.similarity_threshold, so that is set to the tenant's own
        // threshold for this query only (SET LOCAL, undone when the transaction ends).
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync("SELECT set_config('pg_trgm.similarity_threshold', @threshold, true)",
            p => p.AddWithValue("threshold", (isShortQuery
                ? settings.Search_SecondarySimilarityThreshold
                : settings.Search_PrimarySimilarityThreshold).ToString(System.Globalization.CultureInfo.InvariantCulture)), ct);
        return await conn.QueryListAsync(sql, p =>
        {
            p.AddWithValue("@query", normalizedQuery);
            // The prefix match is a LIKE, so the user's own % and _ are escaped; the similarity
            // and full-text arms take the plain text.
            p.AddWithValue("@query_prefix", NpgsqlQueryExtensions.EscapeLike(normalizedQuery) + "%");
            p.AddWithValue("@limit", limit);
            p.AddWithValue("@primaryThreshold", settings.Search_PrimarySimilarityThreshold);
            p.AddWithValue("@secondaryThreshold", settings.Search_SecondarySimilarityThreshold);
            if (siteId.HasValue) p.AddWithValue("@site_id", siteId.Value);
            if (types != null && types.Length > 0) p.AddWithValue("@types", types);
        }, MapResult, ct);
    }

    private static SearchResult MapResult(NpgsqlDataReader reader)
    {
        var entityType = reader.GetString("entity_type");
        var resultSiteId = reader.GetNullableGuid("site_id");
        return new SearchResult
        {
            Type = entityType,
            Id = reader.GetGuid("entity_id"),
            Title = reader.GetString("title"),
            Subtitle = reader.GetNullableString("subtitle"),
            SiteId = resultSiteId,
            Score = reader.GetDouble("score"),
            UpdatedAt = reader.GetDateTime("updated_at"),
            Permissions = new SearchResultPermissions { CanRead = true, CanEdit = false },
            ResourceTypeKey = reader.GetNullableString("resource_type_key")
        };
    }

    // Resource documents carry their type in a column (migration 1690). Groups still need a
    // lookup: a group row has a resource_type_id but no search_documents facet of its own,
    // and the client routes person groups and space groups to different pages.
    private static string ResourceTypeKeySubquery(string source) => $@"
                COALESCE(
                    {source}.resource_type_key,
                    (SELECT rt.key FROM resource_groups g
                     JOIN resource_types rt ON rt.id = g.resource_type_id
                     WHERE {source}.entity_type = 'group' AND g.id = {source}.entity_id)
                ) AS resource_type_key";

    // Both builders filter with index-served predicates only (fts @@, trigram %, ILIKE) and
    // compute the ranking for the rows that survive. ts_rank, similarity() and lower(title) LIKE
    // in the WHERE clause could not use an index, so every keystroke scanned the whole table.
    private static string Scope(Guid? siteId, string[]? types)
    {
        var where = new List<string>();
        if (siteId.HasValue) where.Add("AND (site_id IS NULL OR site_id = @site_id)");
        if (types != null && types.Length > 0) where.Add("AND entity_type = ANY(@types)");
        return string.Join(" ", where);
    }

    private static string BuildCombinedSearchSql(string[]? types, Guid? siteId) => $@"
            WITH matched AS (
                SELECT entity_type, entity_id, title, subtitle, site_id, resource_type_key, updated_at,
                    COALESCE(ts_rank(fts, plainto_tsquery('simple', @query)), 0) AS fts_score,
                    GREATEST(similarity(title, @query), similarity(COALESCE(keywords, ''), @query) * 0.8) AS trgm_score,
                    fts @@ plainto_tsquery('simple', @query) AS fts_match
                FROM search_documents
                WHERE (fts @@ plainto_tsquery('simple', @query) OR title % @query OR keywords % @query)
                  {Scope(siteId, types)}
            )
            SELECT entity_type, entity_id, title, subtitle, site_id,
                (fts_score * 10 + trgm_score)::float8 AS score, updated_at,{ResourceTypeKeySubquery("matched")}
            FROM matched
            WHERE fts_match OR trgm_score > @primaryThreshold
            ORDER BY score DESC, updated_at DESC LIMIT @limit";

    private static string BuildTrigramOnlySql(string[]? types, Guid? siteId) => $@"
            SELECT entity_type, entity_id, title, subtitle, site_id,
                GREATEST(
                    CASE WHEN title ILIKE @query_prefix THEN 1.0 ELSE 0.0 END,
                    similarity(title, @query),
                    similarity(COALESCE(keywords, ''), @query) * 0.8
                )::float8 AS score, updated_at,{ResourceTypeKeySubquery("search_documents")}
            FROM search_documents
            WHERE (title ILIKE @query_prefix OR title % @query OR keywords % @query)
              {Scope(siteId, types)}
            ORDER BY score DESC, updated_at DESC LIMIT @limit";
}
