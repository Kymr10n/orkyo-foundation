using System.Text.RegularExpressions;
using Xunit;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Guards the single list-response shape. Four had accumulated — <c>PagedResult{T}</c>'s
/// <c>{items, page, pageSize, totalItems, …}</c>, the resources list's <c>{data, total, page,
/// pageSize}</c>, the feedback admin list's <c>{items, total}</c> with <c>limit</c>/<c>offset</c>
/// parameters, and the audit endpoints' <c>{events, totalCount}</c> — so a client had to learn a
/// different envelope per endpoint. Everything but the grandfathered audit shape is now
/// <c>PagedResult{T}</c>, paged by <c>page</c>/<c>pageSize</c> through
/// <see cref="Api.Models.PageRequest.From"/>.
///
/// <para>Reporting keeps its own <c>ReportingPagedResult{T}</c>: <c>/api/reporting/v1</c> is a
/// versioned contract for external BI tools. It is a typed record with the same field names
/// rather than an anonymous body, so it does not trip the envelope fact and needs no exemption.</para>
///
/// <para>This is a source-level ratchet, the same idiom as <see cref="ErrorShapeContractTests"/>:
/// it fails on the two ways a fifth shape gets introduced — taking row bounds as bare
/// <c>limit</c>/<c>offset</c> query parameters, or hand-building the envelope as an anonymous
/// object. Each exemption states its reason, and a reverse fact fails when an exempt file stops
/// offending, so the lists only shrink.</para>
/// </summary>
public partial class PaginationShapeContractTests
{
    /// <summary>
    /// Endpoints allowed to take a bare row limit, each for a stated reason.
    /// </summary>
    private static readonly HashSet<string> ExemptLimitFiles = new(StringComparer.Ordinal)
    {
        // Global search is a relevance-ranked union across six entity types feeding a typeahead.
        // Offset paging over a merged ranking is meaningless, so it answers with a capped plain
        // list; the clamp is PageRequest.ClampLimit rather than a hand-written Math.Min.
        "Endpoints/SearchEndpoints.cs",

        // MCP list tools answer an agent, not a pager. There is no "next page" for a model to
        // click, so they take a row limit and say whether the cap cut the list — `Truncated` on
        // RequestListResult and ResourceListResult. Both clamp with PageRequest.ClampLimit, so
        // the shared rule about what a limit may be still applies; only the envelope differs.
        "PlatformApi/Mcp/ScheduleTools.cs",
    };

    /// <summary>
    /// Files allowed to hand-build a list envelope, each for a stated reason.
    /// </summary>
    private static readonly HashSet<string> ExemptEnvelopeFiles = new(StringComparer.Ordinal)
    {
        // The audit trail's {events, totalCount} shape is grandfathered: it predates
        // PagedResult and the admin audit views read those two names. Renaming them is a wire
        // break for no gain that the envelope fields do not already give these endpoints.
        "Endpoints/Admin/AuditEndpoints.cs",
        "Endpoints/TenantAuditEndpoints.cs",
    };

    // A raw row bound as an endpoint parameter — the pre-PageRequest way of paging.
    // The `?` is optional: an earlier version required it, so `int limit = 50` slipped past in
    // an MCP tool that then capped silently at 200 with no way for the caller to tell.
    [GeneratedRegex(@"\bint\??\s+(?:limit|offset)\b")]
    private static partial Regex BareLimitParameterRegex();

    // An anonymous body whose first member names a list — the hand-rolled envelope.
    [GeneratedRegex(@"new\s*\{\s*(?:items|data|results|events)\s*=")]
    private static partial Regex AdHocListEnvelopeRegex();

    [Fact]
    public void NoEndpoint_TakesABareLimitOrOffsetParameter()
    {
        BareLimitParameterRegex().IsMatch("        int? limit = null,")
            .Should().BeTrue("the guard regex must match its own exemplar");
        BareLimitParameterRegex().IsMatch("        int? pageSize = null,")
            .Should().BeFalse("page/pageSize is the convention, not the offence");

        var offenders = ScanSources(BareLimitParameterRegex())
            .Where(rel => !ExemptLimitFiles.Contains(rel))
            .ToList();

        offenders.Should().BeEmpty(
            "these endpoints page with bare limit/offset parameters. Take page/pageSize and "
            + "build the request with PageRequest.From so one envelope and one clamp serve every "
            + "list. A list that genuinely cannot be offset-paged needs an ExemptLimitFiles "
            + "entry with its reason:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoSourceFile_BuildsAnAdHocListEnvelope()
    {
        AdHocListEnvelopeRegex().IsMatch("return Results.Ok(new { data = items, total });")
            .Should().BeTrue("the guard regex must match its own exemplar");
        AdHocListEnvelopeRegex().IsMatch("new SearchResponse { Query = q, Results = withPermissions }")
            .Should().BeFalse("a typed response record is not an ad-hoc envelope");

        var offenders = ScanSources(AdHocListEnvelopeRegex())
            .Where(rel => !ExemptEnvelopeFiles.Contains(rel))
            .ToList();

        offenders.Should().BeEmpty(
            "these files hand-build a list envelope as an anonymous object. Return "
            + "PagedResult<T> (PagedResult<T>.Create for a page, .Capped for a capped whole "
            + "list) so every client reads one set of names. A preserved legacy shape needs an "
            + "ExemptEnvelopeFiles entry with its reason:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void LimitExemptions_HaveNoStaleEntries()
    {
        var stillOffending = ScanSources(BareLimitParameterRegex()).ToHashSet(StringComparer.Ordinal);

        var stale = ExemptLimitFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these files no longer take a bare limit/offset — remove them from ExemptLimitFiles "
            + "so the ratchet moves forward:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void EnvelopeExemptions_HaveNoStaleEntries()
    {
        var stillOffending = ScanSources(AdHocListEnvelopeRegex()).ToHashSet(StringComparer.Ordinal);

        var stale = ExemptEnvelopeFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these files no longer hand-build a list envelope — remove them from "
            + "ExemptEnvelopeFiles so the ratchet moves forward:\n  " + string.Join("\n  ", stale));
    }

    private static List<string> ScanSources(Regex forbidden)
    {
        // backend/src only, deliberately. This rule is about caller-facing list entry points, and
        // they all live here. Scanning backend/core as well reported nine internal helpers whose
        // `int limit` is an ordinary argument — a buffer bound, a row cap passed down by a service
        // that already took page/pageSize above it. A guard that cries wolf on those gets ignored.
        var srcDir = TestRepoPaths.FindDirectory("backend", "src");
        srcDir.Should().NotBeNull("could not locate backend/src");

        var files = Directory.GetFiles(srcDir!, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        files.Should().NotBeEmpty("the source scan found no .cs files — did the layout move?");

        return files
            .Select(f => (Rel: Path.GetRelativePath(srcDir!, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .Where(x => forbidden.IsMatch(x.Text))
            .Select(x => x.Rel)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
