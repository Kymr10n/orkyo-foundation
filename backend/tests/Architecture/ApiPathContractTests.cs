using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Frontend↔backend route contract. Every <c>/api</c> path literal in
/// <c>frontend/src/lib/core/api-paths.ts</c> must correspond to a route actually registered on
/// the live endpoint graph, compared after normalizing away route-parameter syntax
/// (<c>${id}</c> vs <c>{id:guid}</c>), query strings, trailing slashes and case.
///
/// <para>This exists because commit bbf1a73 renamed <c>/api/groups/{groupId:guid}/capabilities</c>
/// to <c>/api/resource-groups/...</c> and left api-paths.ts behind: every group-capability call
/// 404'd in production for ten weeks while three test layers stayed green — the path test pinned
/// the old literal, the api-module test asserted against the constant itself (a tautology), and
/// the component test mocked the module. None of them could see the backend.</para>
///
/// <para>Direction is deliberate — frontend ⊆ backend only. The backend legitimately owns routes
/// this shared frontend never calls (<c>/api/reporting/v1</c> for external consumers,
/// <c>/api/auth/bff</c>, <c>/api/contact</c>, <c>/api/audit</c>). The reverse assertion would need
/// an allowlist larger than the guard and would fire on every endpoint added before its UI lands.</para>
///
/// <para>Ratchet in both directions: a frontend path with no backend route fails, and a
/// <see cref="SaasOwnedPaths"/> entry that Foundation starts serving (or that leaves api-paths.ts)
/// also fails, so the escape hatch cannot quietly outlive its reason.</para>
/// </summary>
[Collection("Database collection")]
public partial class ApiPathContractTests
{
    private readonly DatabaseFixture _fixture;

    public ApiPathContractTests(DatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Normalized paths that api-paths.ts owns but Foundation's backend does not register, because
    /// the endpoint lives in orkyo-saas and the frontend is shared across both editions. Each entry
    /// names the saas file that serves it. A path that exists in NEITHER repo is a bug, not an
    /// allowlist entry. Verified against orkyo-saas on 2026-07-28.
    /// </summary>
    private static readonly HashSet<string> SaasOwnedPaths = new(StringComparer.Ordinal)
    {
        // orkyo-saas/backend/src/Endpoints/TenantEndpoints.cs — tenant self-service
        "/api/tenants",
        "/api/tenants/can-create",
        "/api/tenants/starter-templates",
        "/api/tenants/memberships",
        "/api/tenants/{}",
        "/api/tenants/{}/leave",
        "/api/tenants/{}/cancel-deletion",
        "/api/tenants/{}/transfer-ownership",

        // orkyo-saas/backend/src/Endpoints/Admin/TenantAdminEndpoints.cs — site-admin tenant CRUD
        "/api/admin/tenants",
        "/api/admin/tenants/{}",
        "/api/admin/tenants/{}/tier",

        // orkyo-saas/backend/src/Endpoints/Admin/MembershipAdminEndpoints.cs
        "/api/admin/tenants/{}/members",
        "/api/admin/tenants/{}/members/{}",

        // orkyo-saas/backend/src/Endpoints/Admin/QuotaAdminEndpoints.cs — tier/quota administration
        "/api/admin/tenants/usage",
        "/api/admin/tenants/{}/quotas",
        "/api/admin/tenants/{}/quota-overrides/{}",
        "/api/admin/subscription-tiers",
        "/api/admin/subscription-tiers/{}/quotas/{}",

        // orkyo-saas/backend/src/Endpoints/Admin/BreakGlassEndpoints.cs — support break-glass
        "/api/admin/break-glass/entry",
        "/api/admin/break-glass/exit",
        "/api/admin/break-glass/renew",
        "/api/admin/break-glass/session/{}",
    };

    // Matches a single-quoted or backticked literal that starts with /api. Comments and JSDoc
    // cannot match: they contain no quoted /api literal. Verified against the file's actual shape
    // (no double-quoted literals, no ${API_PATHS...} self-composition).
    [GeneratedRegex(@"(['`])(/api[^'`]*)\1")]
    private static partial Regex ApiPathLiteralRegex();

    // TypeScript interpolation: ${id}, ${encodeURIComponent(slug)} — no braces inside, by convention.
    [GeneratedRegex(@"\$\{[^{}]*\}")]
    private static partial Regex TsInterpolationRegex();

    // Backend route parameter incl. its constraint: {id:guid}, {slug}, {**catchAll}.
    [GeneratedRegex(@"\{[^{}/]*\}")]
    private static partial Regex RouteParamRegex();

    /// <summary>
    /// Collapses both dialects onto one comparable key. Interpolation must be replaced BEFORE the
    /// route-param pass, or the inner braces are consumed first and a stray <c>$</c> is left behind.
    /// </summary>
    private static string Normalize(string path)
    {
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];

        path = TsInterpolationRegex().Replace(path, "{}");
        path = RouteParamRegex().Replace(path, "{}");

        path = path.TrimEnd('/');
        return path.Length == 0 ? "/" : path.ToLowerInvariant();
    }

    [Fact]
    public void EveryFrontendApiPath_IsRegisteredByTheBackend()
    {
        var backendPaths = BackendRoutePaths();

        var offenders = FrontendApiPaths()
            .Where(p => !backendPaths.Contains(p.Normalized) && !SaasOwnedPaths.Contains(p.Normalized))
            .Select(p => $"{p.Raw}   (normalized: {p.Normalized})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "these paths in frontend/src/lib/core/api-paths.ts match no route registered on the "
            + "backend — every request built from them 404s at runtime. In order of likelihood:\n"
            + "  (1) the backend route was renamed and api-paths.ts was not updated — grep "
            + "backend/src/Endpoints for the resource, fix the literal here, AND fix the frontend "
            + "test that pins the old literal;\n"
            + "  (2) the frontend entry is dead — delete it;\n"
            + "  (3) the endpoint genuinely lives in orkyo-saas — add it to SaasOwnedPaths with a "
            + "comment naming the saas file that serves it.\n"
            + "Do NOT add to SaasOwnedPaths to silence a path that exists in neither repo.\n"
            + "Offenders:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void SaasOwnedPaths_AreStillAbsentFromFoundationAndPresentInTheFrontend()
    {
        var backendPaths = BackendRoutePaths();
        var frontendPaths = FrontendApiPaths().Select(p => p.Normalized).ToHashSet(StringComparer.Ordinal);

        var nowServed = SaasOwnedPaths.Where(backendPaths.Contains)
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        var phantom = SaasOwnedPaths.Where(s => !frontendPaths.Contains(s))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

        nowServed.Should().BeEmpty(
            "Foundation now registers these routes, so the saas escape hatch no longer applies — "
            + "remove them from SaasOwnedPaths so the contract is checked for real:\n  "
            + string.Join("\n  ", nowServed));

        phantom.Should().BeEmpty(
            "these SaasOwnedPaths entries no longer appear in api-paths.ts (renamed or deleted) — "
            + "delete them so the allowlist stays an accurate record of the saas surface:\n  "
            + string.Join("\n  ", phantom));
    }

    [Fact]
    public void Extraction_FindsTheExpectedShapeOfPaths()
    {
        // Anti-vacuity. If api-paths.ts moves or changes literal style, the regex could silently
        // extract nothing and the contract test above would pass while checking nothing at all.
        var paths = FrontendApiPaths();
        var normalized = paths.Select(p => p.Normalized).ToList();

        paths.Should().HaveCountGreaterThan(100,
            "api-paths.ts declares well over 100 /api literals — a smaller count means the file "
            + "moved or the extraction regex no longer matches its literal style");

        normalized.Should().Contain("/api/session/me", "plain string literals must be extracted verbatim");
        normalized.Should().Contain("/api/sites/{}/requests", "interpolated paths must normalize to the parameterized form");
        normalized.Should().NotContain(s => s.Contains('?') || s.Contains('$'),
            "normalization must strip query strings and TypeScript interpolation");

        BackendRoutePaths().Should().Contain("/api/resource-groups/{}/capabilities",
            "the route whose rename caused this test to exist must be discoverable from "
            + "EndpointDataSource in normalized form");
    }

    private sealed record FrontendPath(string Raw, string Normalized);

    private static List<FrontendPath> FrontendApiPaths()
    {
        var dir = TestRepoPaths.FindDirectory("frontend", "src", "lib", "core");
        dir.Should().NotBeNull("could not locate frontend/src/lib/core from " + AppContext.BaseDirectory);

        var file = Path.Combine(dir!, "api-paths.ts");
        File.Exists(file).Should().BeTrue($"expected the shared path constants at {file}");

        return ApiPathLiteralRegex().Matches(File.ReadAllText(file))
            .Select(m => m.Groups[2].Value)
            .Select(raw => new FrontendPath(raw, Normalize(raw)))
            .ToList();
    }

    private HashSet<string> BackendRoutePaths()
    {
        var dataSource = _fixture.Factory.Services.GetRequiredService<EndpointDataSource>();
        var paths = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Where(p => p.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            .Select(Normalize)
            .ToHashSet(StringComparer.Ordinal);

        paths.Should().NotBeEmpty("the endpoint graph exposed no /api routes — did app wiring move?");
        return paths;
    }
}
