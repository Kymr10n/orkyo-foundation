using System.Text.RegularExpressions;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Ratchets for the single shapes the codebase converged on: data access from endpoints, one
/// cache, one error body and one list envelope. Each was a hand-rolled scan in its own class
/// until 2026-09; they are rows now, so every guard gets the same exemplar and stale-list checks.
/// </summary>
public partial class ConventionContractTests
{
    // ── endpoint data access ─────────────────────────────────────────────────

    /// <summary>
    /// Endpoint handlers reach the database through repositories. Raw ADO.NET, or an
    /// <c>NpgsqlQueryExtensions</c> query on the handler's own connection, bypasses the tested
    /// data layer and grows god-class endpoints. These files predate the rule; shrink on touch.
    /// </summary>
    private static readonly HashSet<string> KnownEndpointDataAccessFiles = new(StringComparer.Ordinal)
    {
        "src:Endpoints/Admin/AuditEndpoints.cs",
        "src:Endpoints/Admin/DiagnosticsAdminEndpoints.cs",
        "src:Endpoints/ContactEndpoints.cs",
        "src:Endpoints/QuotaEndpoints.cs",
        "src:Endpoints/TenantAuditEndpoints.cs",
    };

    [GeneratedRegex(@"new\s+NpgsqlCommand|NpgsqlDataReader|\.OpenAsync\(|(?<![\w.])(?:conn|db)\.(?:QueryPagedAsync|QueryCappedAsync|ExecuteAsync|QueryListAsync|QuerySingleOrDefaultAsync)\(")]
    private static partial Regex EndpointDataAccessRegex();

    // ── one cache ────────────────────────────────────────────────────────────

    // Five process-wide caches had accumulated, each with its own TTL handling and a ClearCache()
    // only the tests called. They are one injected SingleFlightCache over the host's IMemoryCache,
    // which a test host empties in one call and a product sizes once. No allowlist, on purpose.

    [GeneratedRegex(@"static\s+(?:readonly\s+)?MemoryCache\b")]
    private static partial Regex StaticMemoryCacheRegex();

    [GeneratedRegex(@"static\s+(?:readonly\s+)?ConcurrentDictionary<[^>]*(?:Cache|Entry)")]
    private static partial Regex StaticCacheDictionaryRegex();

    // ── one error body (#96) ─────────────────────────────────────────────────

    /// <summary>
    /// /api/reporting/v1 is a versioned contract for external BI tools. Changing its
    /// {error, message} bodies would break them with no deprecation window, so the reporting
    /// surface keeps its own shape. Nothing in the Orkyo frontends calls it.
    /// </summary>
    private static readonly HashSet<string> ReportingErrorShapeFiles = new(StringComparer.Ordinal)
    {
        "src:Reporting/Auth/ReportingTokenAuthHandler.cs",
        "src:Endpoints/Reporting/ReportingEndpoints.cs",
    };

    /// <summary>Files allowed to call <c>Results.Json</c> directly, each for a stated reason.</summary>
    private static readonly HashSet<string> ResultsJsonExemptFiles = new(StringComparer.Ordinal)
    {
        // The canonical problem-shape helper itself: ProblemResults bottoms out here.
        "src:Helpers/OrkyoProblemDetails.cs",
        // Success payloads exported for humans: WriteIndented + camelCase download bodies,
        // not error responses — Results.Ok would lose the formatting.
        "src:Endpoints/PresetEndpoints.cs",
        "src:Endpoints/ExportEndpoints.cs",
        "src:Endpoints/AccountLifecycleEndpoints.cs",
        // The reporting surface's own error shape (see ReportingErrorShapeFiles).
        "src:Endpoints/Reporting/ReportingEndpoints.cs",
    };

    // An anonymous body whose first member is `error = ...` — the old hand-rolled shape.
    [GeneratedRegex(@"new\s*\{\s*error\s*=")]
    private static partial Regex AnonymousErrorBodyRegex();

    // Framework ValidationProblem: RFC 7807 but without the `code` extension the frontend needs.
    [GeneratedRegex(@"Results\.ValidationProblem\s*\(")]
    private static partial Regex FrameworkValidationProblemRegex();

    // Raw Results.Json: the escape hatch every one of the five historical shapes used.
    [GeneratedRegex(@"Results\.Json\s*\(")]
    private static partial Regex RawResultsJsonRegex();

    // ── one list envelope ────────────────────────────────────────────────────

    /// <summary>Endpoints allowed to take a bare row limit, each for a stated reason.</summary>
    private static readonly HashSet<string> BareLimitExemptFiles = new(StringComparer.Ordinal)
    {
        // Global search is a relevance-ranked union across six entity types feeding a typeahead.
        // Offset paging over a merged ranking is meaningless, so it answers with a capped plain
        // list; the clamp is PageRequest.ClampLimit rather than a hand-written Math.Min.
        "src:Endpoints/SearchEndpoints.cs",

        // MCP list tools answer an agent, not a pager. There is no "next page" for a model to
        // click, so they take a row limit and say whether the cap cut the list — `Truncated` on
        // RequestListResult and ResourceListResult. Both clamp with PageRequest.ClampLimit, so
        // the shared rule about what a limit may be still applies; only the envelope differs.
        "src:PlatformApi/Mcp/ScheduleTools.cs",
    };

    /// <summary>Files allowed to hand-build a list envelope, each for a stated reason.</summary>
    private static readonly HashSet<string> ListEnvelopeExemptFiles = new(StringComparer.Ordinal)
    {
        // The audit trail's {events, totalCount} shape predates PagedResult and the admin audit
        // views read those two names. Renaming them is a wire break for no gain.
        "src:Endpoints/Admin/AuditEndpoints.cs",
    };

    // A raw row bound as an endpoint parameter — the pre-PageRequest way of paging. The `?` is
    // optional: an earlier version required it, so `int limit = 50` slipped past in an MCP tool
    // that then capped silently at 200 with no way for the caller to tell.
    [GeneratedRegex(@"\bint\??\s+(?:limit|offset)\b")]
    private static partial Regex BareLimitParameterRegex();

    // An anonymous body whose first member names a list — the hand-rolled envelope.
    [GeneratedRegex(@"new\s*\{\s*(?:items|data|results|events)\s*=")]
    private static partial Regex AdHocListEnvelopeRegex();

    // The list rows scan backend/src only, deliberately. The rule is about caller-facing list
    // entry points, and they all live there. Scanning backend/core as well reported nine internal
    // helpers whose `int limit` is an ordinary argument, and a guard that cries wolf gets ignored.

    private static IEnumerable<Ratchet> ShapeRatchets() =>
    [
        new("EndpointDataAccess", EndpointDataAccessRegex(), ["src"],
            Scope: f => f.Rel.StartsWith("Endpoints/", StringComparison.Ordinal),
            Baseline: KnownEndpointDataAccessFiles,
            Exemplars:
            [
                new("await using var cmd = new NpgsqlCommand(sql, conn);"),
                new("var page = await conn.QueryPagedAsync(countSql, pageSql, Map, request, ct);"),
                new("return await EndpointHelpers.ExecuteAsync(request, validator, handler);", false,
                    "the validation wrapper is not data access"),
            ],
            ForbidMessage: "endpoint handlers reach the database through a repository, not raw ADO.NET "
                + "or a query on their own connection. The grandfathered files are in "
                + "KnownEndpointDataAccessFiles and shrink on touch."),

        new("StaticMemoryCache", StaticMemoryCacheRegex(), ["src", "core"],
            Exemplars: [new("private static readonly MemoryCache _principalCache = new(...);")],
            ForbidMessage: "a service or middleware does not own a process-wide MemoryCache: inject "
                + "SingleFlightCache and prefix your keys. A static cache cannot be emptied by a "
                + "test host and cannot be sized by a product."),

        new("StaticCacheDictionary", StaticCacheDictionaryRegex(), ["src", "core"],
            Exemplars: [new("private static readonly ConcurrentDictionary<string, TenantResolverCacheEntry> _cache = new();")],
            ForbidMessage: "a ConcurrentDictionary of cache entries re-implements expiry and eviction "
                + "that SingleFlightCache already has. Inject it instead."),

        new("AnonymousErrorBody", AnonymousErrorBodyRegex(), ["src"],
            Exempt: ReportingErrorShapeFiles,
            Exemplars:
            [
                new("return Results.BadRequest(new { error = \"bad\" });"),
                new("return ErrorResponses.BadRequest(\"bad\");", false, "the canonical helper is the rule"),
            ],
            ForbidMessage: "an anonymous `new { error = ... }` body is not the canonical problem shape. "
                + "Use ErrorResponses.* (or ProblemResults.Problem for an uncommon status/code) so "
                + "every client sees one shape with a machine-readable `code`."),

        new("FrameworkValidationProblem", FrameworkValidationProblemRegex(), ["src"],
            Exemplars: [new("return Results.ValidationProblem(result.ToDictionary());")],
            ForbidMessage: "Results.ValidationProblem emits a ProblemDetails WITHOUT the `code` extension "
                + "the frontend switches on. Route validation failures through "
                + "EndpointHelpers.ExecuteAsync, or ProblemResults.Problem(..., errors: ...)."),

        new("RawResultsJson", RawResultsJsonRegex(), ["src"],
            Exempt: ResultsJsonExemptFiles,
            Exemplars: [new("return Results.Json(new[] { conflict });")],
            ForbidMessage: "error bodies go through ErrorResponses.* / ProblemResults.Problem, success "
                + "bodies through Results.Ok. A deliberate exception needs a ResultsJsonExemptFiles "
                + "entry with its reason."),

        new("BareLimitParameter", BareLimitParameterRegex(), ["src"],
            Exempt: BareLimitExemptFiles,
            Exemplars:
            [
                new("        int? limit = null,"),
                new("        int? pageSize = null,", false, "page/pageSize is the convention, not the offence"),
            ],
            ForbidMessage: "take page/pageSize and build the request with PageRequest.From, so one "
                + "envelope and one clamp serve every list. A list that cannot be offset-paged "
                + "needs a BareLimitExemptFiles entry with its reason."),

        new("AdHocListEnvelope", AdHocListEnvelopeRegex(), ["src"],
            Exempt: ListEnvelopeExemptFiles,
            Exemplars:
            [
                new("return Results.Ok(new { data = items, total });"),
                new("new SearchResponse { Query = q, Results = withPermissions }", false,
                    "a typed response record is not an ad-hoc envelope"),
            ],
            ForbidMessage: "return PagedResult<T> (PagedResult<T>.Create for a page, .Capped for a "
                + "capped whole list) so every client reads one set of names. A preserved legacy "
                + "shape needs a ListEnvelopeExemptFiles entry with its reason."),
    ];
}
