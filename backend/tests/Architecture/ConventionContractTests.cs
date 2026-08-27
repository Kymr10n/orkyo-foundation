using System.Text.RegularExpressions;
using Xunit;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Source-level ratchets for the drift classes the 2026-08 review fixed, so they cannot
/// silently recur. Each guard follows the house pattern: a forbid-with-empty-baseline where
/// the sweep finished, a file-path baseline plus a reverse staleness test where legacy
/// sites are grandfathered, and an exemplar assert so a regex that rots fails loudly
/// instead of matching nothing (the ApiPathContractTests anti-vacuity rule).
///
/// Unlike ErrorShapeContractTests this scans backend/core and backend/seeding as well as
/// backend/src — the earlier scan's src-only root is why most of the drift lived
/// unnoticed in core, where most of the code is.
/// </summary>
public partial class ConventionContractTests
{
    // ── (a) KeyNotFoundException ─────────────────────────────────────────────

    [GeneratedRegex(@"throw\s+new\s+KeyNotFoundException")]
    private static partial Regex KeyNotFoundThrowRegex();

    [Fact]
    public void NoSourceFile_ThrowsKeyNotFoundException()
    {
        KeyNotFoundThrowRegex().IsMatch("throw new KeyNotFoundException(\"x\")")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"), ("backend", "seeding"))
            .Where(x => KeyNotFoundThrowRegex().IsMatch(x.Text))
            .Select(x => x.Rel).ToList();

        offenders.Should().BeEmpty(
            "\"no such resource\" is NotFoundException (mapped to 404); a BCL "
            + "KeyNotFoundException is a programming error and falls through to a 500 "
            + "(AppExceptionHandlerTests pins that). For an internal catalog miss use "
            + "InvalidOperationException. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    // ── (b) ^/$ anchors in validator patterns ────────────────────────────────

    // A string literal starting with ^ or ending with $ inside a .Matches( call.
    [GeneratedRegex(@"\.Matches\(\s*@?""(\^[^""]*|[^""]*\$)""")]
    private static partial Regex DollarAnchoredMatchesRegex();

    [Fact]
    public void NoValidator_UsesDollarAnchoredPatterns()
    {
        DollarAnchoredMatchesRegex().IsMatch(@".Matches(@""^#[0-9A-Fa-f]{6}$"")")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => x.Rel.Contains("Validators/"))
            .Where(x => DollarAnchoredMatchesRegex().IsMatch(x.Text))
            .Select(x => x.Rel).ToList();

        offenders.Should().BeEmpty(
            "anchor with \\A and \\z, not ^ and $: in .NET `$` also matches before a "
            + "trailing newline, so \"#ffffff\\n\" passes a $-anchored check. Shared "
            + "patterns live in ValidationPatterns / ResourceTypeKeyRules. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    // ── (c) param-style upserts ──────────────────────────────────────────────

    /// <summary>
    /// Legacy files still writing <c>DO UPDATE SET col = @param</c>. The convention is
    /// EXCLUDED (docs/conventions.md, "Upserts"); these predate it and shrink on touch.
    /// Do not add files here — write EXCLUDED in new code.
    /// </summary>
    private static readonly HashSet<string> KnownParamUpsertFiles = new(StringComparer.Ordinal)
    {
        "core:Repositories/SchedulingRepository.cs",
        "core:Repositories/AiAllowanceRepository.cs",
        "core:Repositories/AiCredentialRepository.cs",
        "core:Repositories/UserPreferencesRepository.cs",
        "core:Services/InvitationService.cs",
        "core:Services/Preset/PresetApplier.cs",
    };

    // DO UPDATE SET followed by an @param assignment before the statement ends.
    [GeneratedRegex(@"DO UPDATE SET[^;""]*?=\s*@\w+", RegexOptions.Singleline)]
    private static partial Regex ParamUpsertRegex();

    [Fact]
    public void NoNewFile_WritesParamStyleUpserts()
    {
        ParamUpsertRegex().IsMatch("ON CONFLICT (key) DO UPDATE SET value = @value, updated_at = NOW()")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => ParamUpsertRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !KnownParamUpsertFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "upserts read the inserted value via EXCLUDED.col, not the parameter that "
            + "happens to hold it (docs/conventions.md). The grandfathered files are in "
            + "KnownParamUpsertFiles and shrink on touch. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void ParamUpsertBaseline_HasNoStaleEntries()
    {
        var stillOffending = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => ParamUpsertRegex().IsMatch(x.Text))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        var stale = KnownParamUpsertFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these baseline entries no longer contain a param-style upsert — remove them "
            + "so the ratchet moves forward and cannot silently regress:\n  "
            + string.Join("\n  ", stale));
    }

    // ── (d) raw config indexer with a fallback ───────────────────────────────

    /// <summary>
    /// The empty-env bug class: <c>configuration[key] ?? fallback</c> substitutes only for
    /// null, but the deploy pipeline writes <c>KEY=</c> for every unset key — the empty
    /// string sails past <c>??</c> and silently replaces the intended value (the BFF
    /// cookie-name bug). Required values go through <c>GetRequired*</c>, optional ones
    /// through <c>GetOptionalString</c>/<c>IsSet</c>; there is no fallback helper on
    /// purpose — required config fails at startup instead of defaulting.
    /// </summary>
    [GeneratedRegex(@"\bconfig(uration)?\[[^\]]+\]\s*\?\?")]
    private static partial Regex ConfigFallbackRegex();

    private static readonly HashSet<string> ConfigFallbackExemptFiles = new(StringComparer.Ordinal)
    {
        // The primitive itself: GetOptionalString normalizes null to "" with `?? ""`.
        "core:Configuration/ConfigurationExtensions.cs",
    };

    [Fact]
    public void NoSourceFile_FallsBackOnARawConfigRead()
    {
        ConfigFallbackRegex().IsMatch("var x = configuration[ConfigKeys.Foo] ?? \"bar\";")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => ConfigFallbackRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !ConfigFallbackExemptFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "`configuration[key] ?? fallback` misses empty values (the .env writes KEY= "
            + "for unset keys) and hides missing required config. Use GetRequired* for "
            + "required values (fail at startup) or GetOptionalString/IsSet for optional "
            + "ones — never a compiled fallback. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    // ── (e) SQL status literals ──────────────────────────────────────────────

    /// <summary>
    /// Legacy files with <c>'active'</c>/<c>'admin'</c>/<c>'keycloak'</c> as SQL literals
    /// where MembershipStatusConstants / RoleConstants / the provider name belong.
    /// Grandfathered; shrink on touch; new files use the constants.
    /// </summary>
    private static readonly HashSet<string> KnownSqlLiteralFiles = new(StringComparer.Ordinal)
    {
        "core:Repositories/TenantControlPlaneRepository.cs",
        "core:Repositories/PlatformUserRepository.cs",
        "core:Integrations/Keycloak/KeycloakIdentityLinkService.cs",
        "core:Services/InvitationService.cs",
        "core:Services/UserManagementService.cs",
        "core:Services/SessionService.cs",
        "core:Services/UserProvisioningService.cs",
        "core:Services/UserLifecycleService.cs",
        "core:Services/AnnouncementBroadcastService.cs",
        "src:Endpoints/QuotaEndpoints.cs",
        "src:Endpoints/Admin/UserAdminEndpoints.cs",
    };

    private static readonly HashSet<string> SqlLiteralExemptFiles = new(StringComparer.Ordinal)
    {
        // Constants files whose doc comments quote the raw values they define.
        "core:Constants/MembershipStatusConstants.cs",
        "core:Constants/UserStatusConstants.cs",
        "core:Constants/RoleConstants.cs",
    };

    [GeneratedRegex(@"'(active|admin|keycloak)'")]
    private static partial Regex SqlStatusLiteralRegex();

    [Fact]
    public void NoNewFile_HardcodesStatusLiteralsInSql()
    {
        SqlStatusLiteralRegex().IsMatch("WHERE status = 'active'")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => SqlStatusLiteralRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !KnownSqlLiteralFiles.Contains(key) && !SqlLiteralExemptFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "'active'/'admin'/'keycloak' in SQL bypass MembershipStatusConstants / "
            + "RoleConstants / the provider constant. Bind a parameter from the constant "
            + "instead. The grandfathered files are in KnownSqlLiteralFiles and shrink on "
            + "touch. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void SqlLiteralBaseline_HasNoStaleEntries()
    {
        var stillOffending = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => SqlStatusLiteralRegex().IsMatch(x.Text))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        var stale = KnownSqlLiteralFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these baseline entries no longer contain a hardcoded status literal — remove "
            + "them so the ratchet moves forward:\n  " + string.Join("\n  ", stale));
    }

    // ── shared scan ──────────────────────────────────────────────────────────

    /// <summary>
    /// Files across the given roots, keyed "<root>:<relative path>" so a baseline entry
    /// is unambiguous when the same file name exists under two roots.
    /// </summary>
    private static List<(string Key, string Rel, string Text)> ScanSources(params (string, string)[] roots)
    {
        var results = new List<(string, string, string)>();
        foreach (var (top, sub) in roots)
        {
            var dir = TestRepoPaths.FindDirectory(top, sub);
            dir.Should().NotBeNull($"could not locate {top}/{sub}");

            var files = Directory.GetFiles(dir!, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList();
            files.Should().NotBeEmpty($"the source scan found no .cs files under {top}/{sub} — did the layout move?");

            results.AddRange(files.Select(f =>
            {
                var rel = Path.GetRelativePath(dir!, f).Replace('\\', '/');
                return ($"{sub}:{rel}", rel, File.ReadAllText(f));
            }));
        }
        return results;
    }
}
