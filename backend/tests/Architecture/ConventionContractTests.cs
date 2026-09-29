using System.Text.RegularExpressions;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Source-level ratchets for the drift classes the 2026-08 review fixed, so they cannot
/// silently recur. Each guard follows the house pattern: a forbid-with-empty-baseline where
/// the sweep finished, a file-path baseline plus a reverse staleness test where legacy
/// sites are grandfathered, and an exemplar assert so a regex that rots fails loudly
/// instead of matching nothing (the ApiPathContractTests anti-vacuity rule).
///
/// Each row names its roots: most scan backend/core as well as backend/src, because an earlier
/// src-only scan is why most of the drift lived unnoticed in core, where most of the code is.
///
/// Every guard is a <see cref="Ratchet"/> row; the two theories at the bottom run them all.
/// </summary>
public partial class ConventionContractTests
{
    // ── (a) KeyNotFoundException ─────────────────────────────────────────────

    [GeneratedRegex(@"throw\s+new\s+KeyNotFoundException")]
    private static partial Regex KeyNotFoundThrowRegex();

    // ── (b) ^/$ anchors in validator patterns ────────────────────────────────

    // A string literal starting with ^ or ending with $ inside a .Matches( call.
    [GeneratedRegex(@"\.Matches\(\s*@?""(\^[^""]*|[^""]*\$)""")]
    private static partial Regex DollarAnchoredMatchesRegex();

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
        "core:Services/SessionService.cs",
        "core:Services/UserProvisioningService.cs",
        "core:Services/UserLifecycleService.cs",
        "core:Services/AnnouncementBroadcastService.cs",
        "src:Endpoints/QuotaEndpoints.cs",
    };

    private static readonly HashSet<string> SqlLiteralExemptFiles = new(StringComparer.Ordinal)
    {
        // Constants files whose doc comments quote the raw values they define.
        "core:Constants/MembershipStatusConstants.cs",
    };

    [GeneratedRegex(@"'(active|admin|keycloak)'")]
    private static partial Regex SqlStatusLiteralRegex();

    private static IEnumerable<Ratchet> DriftRatchets() =>
    [
        new("KeyNotFoundException", KeyNotFoundThrowRegex(), ["src", "core", "seeding"],
            Exemplars: [new("throw new KeyNotFoundException(\"x\")")],
            ForbidMessage: "\"no such resource\" is NotFoundException (mapped to 404); a BCL "
                + "KeyNotFoundException is a programming error and falls through to a 500 "
                + "(AppExceptionHandlerTests pins that). For an internal catalog miss use "
                + "InvalidOperationException."),

        new("DollarAnchoredValidatorPattern", DollarAnchoredMatchesRegex(), ["src", "core"],
            Scope: DeclaresAValidator,
            Exemplars: [new(@".Matches(@""^#[0-9A-Fa-f]{6}$"")")],
            ForbidMessage: "anchor with \\A and \\z, not ^ and $: in .NET `$` also matches before a "
                + "trailing newline, so \"#ffffff\\n\" passes a $-anchored check. Shared "
                + "patterns live in ValidationPatterns / ResourceTypeKeyRules."),

        new("ParamStyleUpsert", ParamUpsertRegex(), ["src", "core"],
            Baseline: KnownParamUpsertFiles,
            Exemplars: [new("ON CONFLICT (key) DO UPDATE SET value = @value, updated_at = NOW()")],
            ForbidMessage: "upserts read the inserted value via EXCLUDED.col, not the parameter that "
                + "happens to hold it (docs/conventions.md). The grandfathered files are in "
                + "KnownParamUpsertFiles and shrink on touch."),

        new("RawConfigFallback", ConfigFallbackRegex(), ["src", "core", "shared"],
            Exempt: ConfigFallbackExemptFiles,
            Exemplars: [new("var x = configuration[ConfigKeys.Foo] ?? \"bar\";")],
            ForbidMessage: "`configuration[key] ?? fallback` misses empty values (the .env writes KEY= "
                + "for unset keys) and hides missing required config. Use GetRequired* for "
                + "required values (fail at startup) or GetOptionalString/IsSet for optional "
                + "ones — never a compiled fallback."),

        new("SqlStatusLiteral", SqlStatusLiteralRegex(), ["src", "core"],
            Baseline: KnownSqlLiteralFiles,
            Exempt: SqlLiteralExemptFiles,
            Exemplars: [new("WHERE status = 'active'")],
            ForbidMessage: "'active'/'admin'/'keycloak' in SQL bypass MembershipStatusConstants / "
                + "RoleConstants / the provider constant. Bind a parameter from the constant "
                + "instead. The grandfathered files are in KnownSqlLiteralFiles and shrink on "
                + "touch."),
    ];

    // ── the ratchet shape and the two theories that run every row ────────────

    /// <summary>
    /// One source guard. A file under <see cref="Roots"/> that passes <see cref="Scope"/> must
    /// not match <see cref="Pattern"/> unless it is grandfathered in <see cref="Baseline"/>
    /// (shrinks on touch) or allowed for a stated reason in <see cref="Exempt"/>. Both lists
    /// fail when an entry stops offending, so neither outlives its reason. The same scope
    /// applies to both checks. Each exemplar pins what the regex must (or must not) match, and
    /// every row has at least one it must match, so a rotted regex fails loudly instead of
    /// matching nothing.
    /// </summary>
    private sealed record Ratchet(
        string Name,
        Regex Pattern,
        string[] Roots,
        Exemplar[] Exemplars,
        string ForbidMessage,
        Func<SourceFile, bool>? Scope = null,
        IReadOnlySet<string>? Baseline = null,
        IReadOnlySet<string>? Exempt = null);

    private sealed record Exemplar(string Text, bool Matches = true, string Because = "the guard regex must match its own exemplar");

    private static Ratchet[] AllRatchets() => [.. DriftRatchets(), .. ConventionRatchets(), .. ShapeRatchets()];

    /// <summary>A validator lives wherever a class declares one, not only under Validators/.</summary>
    private static bool DeclaresAValidator(SourceFile file) =>
        file.Text.Contains("AbstractValidator<", StringComparison.Ordinal);

    private static Ratchet Find(string name) => AllRatchets().Single(r => r.Name == name);

    public static TheoryData<string> RatchetNames => new(AllRatchets().Select(r => r.Name));

    public static TheoryData<string> ListedRatchetNames =>
        new(AllRatchets().Where(r => r.Baseline is not null || r.Exempt is not null).Select(r => r.Name));

    /// <summary>Keys ("root:rel") of the in-scope files that match the ratchet's pattern.</summary>
    private static IEnumerable<string> Matching(Ratchet ratchet) =>
        TestRepoPaths.BackendSources(ratchet.Roots)
            .Where(f => (ratchet.Scope?.Invoke(f) ?? true) && ratchet.Pattern.IsMatch(f.Text))
            .Select(f => f.Key);

    [Theory]
    [MemberData(nameof(RatchetNames))]
    public void NoNewFile_BreaksTheRatchet(string ratchet)
    {
        var r = Find(ratchet);
        r.Exemplars.Should().Contain(e => e.Matches, "every guard needs an exemplar its regex must match");
        foreach (var (text, matches, because) in r.Exemplars)
            r.Pattern.IsMatch(text).Should().Be(matches, because);

        var offenders = Matching(r)
            .Where(key => !(r.Baseline?.Contains(key) ?? false) && !(r.Exempt?.Contains(key) ?? false))
            .ToList();

        offenders.Should().BeEmpty(r.ForbidMessage + " Offenders:\n  " + string.Join("\n  ", offenders));
    }

    [Theory]
    [MemberData(nameof(ListedRatchetNames))]
    public void RatchetLists_HaveNoStaleEntries(string ratchet)
    {
        var r = Find(ratchet);
        var stillOffending = Matching(r).ToHashSet(StringComparer.Ordinal);

        var stale = (r.Baseline ?? new HashSet<string>()).Concat(r.Exempt ?? new HashSet<string>())
            .Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty("these baseline or exempt entries no longer offend — remove them so the "
            + "ratchet moves forward and cannot silently regress:\n  " + string.Join("\n  ", stale));
    }
}
