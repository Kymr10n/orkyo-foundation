using System.Text.RegularExpressions;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Ratchet guard: endpoint handlers must reach the database through repositories
/// (or the shared <c>NpgsqlQueryExtensions</c> helpers), never by opening a
/// connection and issuing raw ADO.NET in the handler. Raw <c>NpgsqlCommand</c> /
/// <c>NpgsqlDataReader</c> / <c>OpenAsync</c> inside <c>backend/src/Endpoints</c>
/// bypasses the tested data layer and grows god-class endpoints.
///
/// <para>
/// A baseline of the files that still do this today is recorded below. The test
/// is a ratchet in both directions:
/// </para>
/// <list type="bullet">
///   <item>A NEW endpoint file with raw ADO.NET (not in the baseline) fails —
///   use a repository / NpgsqlQueryExtensions instead.</item>
///   <item>A baseline file that NO LONGER contains raw ADO.NET fails — it was
///   cleaned up, so remove it from the baseline to lock in the win.</item>
/// </list>
/// See orkyo-infra/docs/plans/optimization-plan-2026-07.md §Guardrails (G2a).
/// </summary>
public partial class EndpointDataAccessTests
{
    /// <summary>
    /// Endpoint files (path relative to <c>backend/src/Endpoints</c>, forward
    /// slashes) that still issue raw ADO.NET. Shrink this as files are cleaned up;
    /// never add to it — new writes go through a repository. Verified against the
    /// tree on 2026-07-12.
    /// </summary>
    private static readonly HashSet<string> KnownRawDataAccessFiles =
    [
        "Admin/DiagnosticsAdminEndpoints.cs",
        "QuotaEndpoints.cs",
    ];

    [GeneratedRegex(@"new\s+NpgsqlCommand|NpgsqlDataReader|\.OpenAsync\(")]
    private static partial Regex RawDataAccessRegex();

    /// <summary>Endpoint files issuing raw ADO.NET, as paths relative to <c>backend/src/Endpoints</c>.</summary>
    private static List<string> RawDataAccessEndpointFiles()
    {
        var endpoints = TestRepoPaths.BackendSources("src")
            .Where(f => f.Rel.StartsWith("Endpoints/", StringComparison.Ordinal))
            .ToList();
        endpoints.Should().NotBeEmpty("the Endpoints scan found no .cs files — did the layout move?");

        return endpoints
            .Where(f => RawDataAccessRegex().IsMatch(f.Text))
            .Select(f => f.Rel["Endpoints/".Length..])
            .ToList();
    }

    [Fact]
    public void NoNewEndpointFile_IssuesRawAdoNet()
    {
        var offenders = RawDataAccessEndpointFiles()
            .Where(rel => !KnownRawDataAccessFiles.Contains(rel))
            .OrderBy(rel => rel, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "these endpoint files issue raw ADO.NET (new NpgsqlCommand / NpgsqlDataReader / " +
            "OpenAsync) in the handler — route data access through a repository or " +
            "NpgsqlQueryExtensions instead:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void BaselineFiles_StillContainRawAdoNet()
    {
        var stillOffending = RawDataAccessEndpointFiles().ToHashSet(StringComparer.Ordinal);

        var stale = KnownRawDataAccessFiles
            .Where(rel => !stillOffending.Contains(rel))
            .OrderBy(rel => rel, StringComparer.Ordinal)
            .ToList();

        stale.Should().BeEmpty(
            "these files are in the raw-ADO.NET baseline but no longer issue raw ADO.NET " +
            "(or were removed) — delete them from KnownRawDataAccessFiles so the ratchet " +
            "can't slip back:\n  " + string.Join("\n  ", stale));
    }
}
