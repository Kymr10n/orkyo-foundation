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
/// See orkyo-infra/docs/optimization-plan-2026-07.md §Guardrails (G2a) and the
/// Wave 3 endpoint refactors (W3.1) that shrink this baseline to empty.
/// </summary>
public partial class EndpointDataAccessTests
{
    /// <summary>
    /// Endpoint files (path relative to <c>backend/src/Endpoints</c>, forward
    /// slashes) that still issue raw ADO.NET. Shrink this as Wave 3 lands; never
    /// add to it — new writes go through a repository. Verified against the tree
    /// on 2026-07-12.
    /// </summary>
    private static readonly HashSet<string> KnownRawDataAccessFiles =
    [
        "Admin/AuditEndpoints.cs",
        "Admin/DiagnosticsAdminEndpoints.cs",
        "Admin/UserAdminEndpoints.cs",
        "QuotaEndpoints.cs",
        "TenantAuditEndpoints.cs",
    ];

    [GeneratedRegex(@"new\s+NpgsqlCommand|NpgsqlDataReader|\.OpenAsync\(")]
    private static partial Regex RawDataAccessRegex();

    [Fact]
    public void NoNewEndpointFile_IssuesRawAdoNet()
    {
        var endpointsDir = FindDirectory("backend", "src", "Endpoints");
        endpointsDir.Should().NotBeNull("could not locate backend/src/Endpoints");

        var files = Directory.GetFiles(endpointsDir!, "*.cs", SearchOption.AllDirectories);
        files.Should().NotBeEmpty("the Endpoints scan found no .cs files — did the layout move?");

        var offenders = files
            .Where(f => RawDataAccessRegex().IsMatch(File.ReadAllText(f)))
            .Select(f => RelativeEndpointPath(endpointsDir!, f))
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
        var endpointsDir = FindDirectory("backend", "src", "Endpoints");
        endpointsDir.Should().NotBeNull("could not locate backend/src/Endpoints");

        var stale = KnownRawDataAccessFiles
            .Where(rel =>
            {
                var path = Path.Combine([endpointsDir!, .. rel.Split('/')]);
                return !File.Exists(path) || !RawDataAccessRegex().IsMatch(File.ReadAllText(path));
            })
            .OrderBy(rel => rel, StringComparer.Ordinal)
            .ToList();

        stale.Should().BeEmpty(
            "these files are in the raw-ADO.NET baseline but no longer issue raw ADO.NET " +
            "(or were removed) — delete them from KnownRawDataAccessFiles so the ratchet " +
            "can't slip back:\n  " + string.Join("\n  ", stale));
    }

    private static string RelativeEndpointPath(string endpointsDir, string file) =>
        Path.GetRelativePath(endpointsDir, file).Replace('\\', '/');

    private static string? FindDirectory(params string[] pathSegments)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12; i++)
        {
            var candidate = Path.Combine([dir, .. pathSegments]);
            if (Directory.Exists(candidate)) return candidate;

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null) break;
            dir = parent;
        }
        return null;
    }
}
