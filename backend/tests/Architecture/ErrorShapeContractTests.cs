using System.Text.RegularExpressions;
using Xunit;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Guards the single error body shape (#96). Five shapes had accumulated —
/// <c>ErrorResponse</c>'s <c>{error, code}</c>, ad-hoc <c>Results.BadRequest(new {...})</c>,
/// the CSRF middleware's <c>{error}</c>, <c>ProblemDetailsHelper</c>'s auth problems, and
/// framework <c>ValidationProblem</c> — so a client had to guess which fields would be present.
/// Everything now goes through <see cref="Api.Helpers.ProblemResults"/> /
/// <see cref="Api.Helpers.ErrorResponses"/>.
///
/// <para>This is a source-level ratchet: it fails on the ways a sixth shape gets introduced —
/// writing an anonymous error body, or calling framework <c>Results.ValidationProblem</c>
/// (which omits our <c>code</c> extension, leaving the frontend unable to switch on it).</para>
/// </summary>
public partial class ErrorShapeContractTests
{
    /// <summary>
    /// Files allowed to emit a non-canonical error body, each for a stated reason. Additions need
    /// the same justification — this is not a place to park new drift.
    /// </summary>
    private static readonly HashSet<string> ExemptFiles = new(StringComparer.Ordinal)
    {
        // /api/reporting/v1 is a VERSIONED contract consumed by external BI tools. Changing its
        // {error, message} bodies would break them with no deprecation window, so the reporting
        // surface deliberately keeps its own shape. Nothing in the Orkyo frontends calls it.
        "Reporting/Auth/ReportingTokenAuthHandler.cs",
        "Endpoints/Reporting/ReportingEndpoints.cs",
        "Endpoints/Reporting/ReportingTokenEndpoints.cs",
    };

    // An anonymous body whose first member is `error = ...` — the old hand-rolled shape.
    [GeneratedRegex(@"new\s*\{\s*error\s*=")]
    private static partial Regex AnonymousErrorBodyRegex();

    // Framework ValidationProblem: RFC 7807 but without the `code` extension the frontend needs.
    [GeneratedRegex(@"Results\.ValidationProblem\s*\(")]
    private static partial Regex FrameworkValidationProblemRegex();

    [Fact]
    public void NoSourceFile_EmitsAHandRolledErrorBody()
    {
        var offenders = ScanSources((rel, text) =>
            AnonymousErrorBodyRegex().IsMatch(text) ? rel : null);

        offenders.Should().BeEmpty(
            "these files build an anonymous `new { error = ... }` response body instead of the "
            + "canonical problem shape. Use ErrorResponses.* (or ProblemResults.Problem for an "
            + "uncommon status/code) so every client sees one shape with a machine-readable "
            + "`code`:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoSourceFile_UsesFrameworkValidationProblem()
    {
        var offenders = ScanSources((rel, text) =>
            FrameworkValidationProblemRegex().IsMatch(text) ? rel : null);

        offenders.Should().BeEmpty(
            "these files call Results.ValidationProblem, which emits a ProblemDetails WITHOUT the "
            + "`code` extension the frontend switches on. Route validation failures through "
            + "EndpointHelpers.ExecuteAsync, or ProblemResults.Problem(..., errors: ...):\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Files allowed to call <c>Results.Json</c> directly. This list is separate from
    /// <see cref="ExemptFiles"/> so a success-payload exemption here does not also exempt
    /// the file from the error-shape facts above.
    /// </summary>
    private static readonly HashSet<string> JsonExemptFiles = new(StringComparer.Ordinal)
    {
        // The canonical problem-shape helper itself: ProblemResults bottoms out here.
        "Helpers/OrkyoProblemDetails.cs",
        // Success payloads exported for humans: WriteIndented + camelCase download bodies,
        // not error responses — Results.Ok would lose the formatting.
        "Endpoints/PresetEndpoints.cs",
        "Endpoints/ExportEndpoints.cs",
    };

    // Raw Results.Json: the escape hatch every one of the five historical shapes used.
    [GeneratedRegex(@"Results\.Json\s*\(")]
    private static partial Regex RawResultsJsonRegex();

    [Fact]
    public void NoSourceFile_CallsResultsJsonDirectly()
    {
        RawResultsJsonRegex().IsMatch("return Results.Json(new[] { conflict });")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources((rel, text) =>
            RawResultsJsonRegex().IsMatch(text) && !JsonExemptFiles.Contains(rel) ? rel : null);

        offenders.Should().BeEmpty(
            "these files call Results.Json directly — error bodies go through ErrorResponses.* / "
            + "ProblemResults.Problem, success bodies through Results.Ok. A deliberate exception "
            + "(formatted download payloads, the problem helper itself) needs a JsonExemptFiles "
            + "entry with its reason:\n  " + string.Join("\n  ", offenders));
    }

    private static List<string> ScanSources(Func<string, string, string?> inspect)
    {
        var srcDir = TestRepoPaths.FindDirectory("backend", "src");
        srcDir.Should().NotBeNull("could not locate backend/src");

        var files = Directory.GetFiles(srcDir!, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        files.Should().NotBeEmpty("the source scan found no .cs files — did the layout move?");

        return files
            .Select(f => (Rel: Path.GetRelativePath(srcDir!, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .Where(x => !ExemptFiles.Contains(x.Rel))
            .Select(x => inspect(x.Rel, x.Text))
            .Where(rel => rel is not null)
            .Select(rel => rel!)
            .OrderBy(rel => rel, StringComparer.Ordinal)
            .ToList();
    }
}
