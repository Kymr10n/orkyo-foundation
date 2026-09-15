using System.Text.RegularExpressions;
using Xunit;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Ratchets for the conventions docs/conventions.md states but that had no guard until the
/// 2026-09 design review measured them (findings B5 and B6). Same house pattern as the first
/// file: a file-key baseline, a forbid-outside-baseline fact, and a reverse staleness fact so
/// an entry that stops offending must be removed and the number only goes down.
/// </summary>
public partial class ConventionContractTests
{
    // ── (f) services that write SQL ──────────────────────────────────────────

    /// <summary>
    /// Services that construct <c>NpgsqlCommand</c> directly: repositories wearing service
    /// names (docs/conventions.md, "Layering"). Grandfathered; move the query into a
    /// repository on touch; new services take a repository.
    /// </summary>
    private static readonly HashSet<string> KnownSqlWritingServiceFiles = new(StringComparer.Ordinal)
    {
        "core:Services/AdminAuditService.cs",
        "core:Services/AnnouncementBroadcastService.cs",
        "core:Services/Insights/InsightsService.cs",
        "core:Services/InvitationService.cs",
        "core:Services/PlatformApi/ApiAccessTokenService.cs",
        "core:Services/Preset/PresetApplier.cs",
        "core:Services/PresetService.cs",
        "core:Services/Reporting/ReportingTokenService.cs",
        "core:Services/SessionService.cs",
        "core:Services/StarterTemplateService.cs",
        "core:Services/TenantResolver.cs",
        "core:Services/TenantUserService.cs",
        "core:Services/UserManagementService.cs",
        "core:Services/UserProvisioningService.cs",
        "core:Services/UserSessionService.cs",
        "core:Services/WorkerJobCoordinator.cs",
    };

    [GeneratedRegex(@"new\s+NpgsqlCommand")]
    private static partial Regex NewNpgsqlCommandRegex();

    [Fact]
    public void NoNewService_WritesSql()
    {
        NewNpgsqlCommandRegex().IsMatch("await using var cmd = new NpgsqlCommand(sql, conn);")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "core"))
            .Where(x => x.Rel.StartsWith("Services/", StringComparison.Ordinal))
            .Where(x => NewNpgsqlCommandRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !KnownSqlWritingServiceFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "services do not write SQL (docs/conventions.md, Layering): put the query in a "
            + "repository and inject it. The grandfathered files are in "
            + "KnownSqlWritingServiceFiles and shrink on touch. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void SqlWritingServiceBaseline_HasNoStaleEntries()
    {
        var stillOffending = ScanSources(("backend", "core"))
            .Where(x => NewNpgsqlCommandRegex().IsMatch(x.Text))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        var stale = KnownSqlWritingServiceFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these services no longer construct NpgsqlCommand — remove them from the baseline "
            + "so the ratchet moves forward:\n  " + string.Join("\n  ", stale));
    }

    // ── (g) `db` connection locals ───────────────────────────────────────────

    /// <summary>
    /// The connection local is <c>conn</c> (docs/conventions.md, "Opening a connection").
    /// Files still using <c>db</c>; rename on touch.
    /// </summary>
    private static readonly HashSet<string> KnownDbLocalFiles = new(StringComparer.Ordinal)
    {
        "core:Repositories/CalendarFeedTokenRepository.cs",
        "core:Repositories/CriteriaRepository.cs",
        "core:Repositories/CriterionApplicabilityRepository.cs",
        "core:Repositories/ListDefinitionRepository.cs",
        "core:Repositories/ListInstanceRepository.cs",
        "core:Repositories/RequestDependencyRepository.cs",
        "core:Repositories/RequestRepository.cs",
        "core:Repositories/ResourceAssignmentRepository.cs",
        "core:Repositories/ResourceCapabilityRepository.cs",
        "core:Repositories/ResourceCustomFieldRepository.cs",
        "core:Repositories/ResourceGroupMemberRepository.cs",
        "core:Repositories/ResourceRepository.cs",
        "core:Repositories/ResourceTypeRepository.cs",
        "core:Services/AnnouncementBroadcastService.cs",
        "core:Services/Insights/InsightsService.cs",
        "core:Services/SessionService.cs",
        "core:Services/UserLifecycleService.cs",
        "core:Services/UserSessionService.cs",
    };

    [GeneratedRegex(@"await\s+using\s+var\s+db\s*=")]
    private static partial Regex DbConnectionLocalRegex();

    [Fact]
    public void NoNewFile_NamesTheConnectionLocalDb()
    {
        DbConnectionLocalRegex().IsMatch("await using var db = connectionFactory.CreateOrgConnection(orgContext);")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => DbConnectionLocalRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !KnownDbLocalFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "the connection local is named `conn` (docs/conventions.md). The grandfathered "
            + "files are in KnownDbLocalFiles and shrink on touch. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void DbLocalBaseline_HasNoStaleEntries()
    {
        var stillOffending = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => DbConnectionLocalRegex().IsMatch(x.Text))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        var stale = KnownDbLocalFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these files no longer name a connection local `db` — remove them from the "
            + "baseline:\n  " + string.Join("\n  ", stale));
    }

    // ── (h) bare numeric length limits in validators ─────────────────────────

    /// <summary>
    /// Length limits come from <c>DomainLimits</c> (docs/conventions.md, "Validation").
    /// Validators still passing a bare number; adopt the constant on touch.
    /// </summary>
    private static readonly HashSet<string> KnownBareLengthLimitFiles = new(StringComparer.Ordinal)
    {
        "core:Validators/AcceptInvitationRequestValidator.cs",
        "core:Validators/AiRequestValidators.cs",
        "core:Validators/ContactRequestValidator.cs",
        "core:Validators/ListRequestValidators.cs",
        "core:Validators/RequestRequirementValidators.cs",
        "core:Validators/ResourceCustomFieldRequestValidators.cs",
        "core:Validators/ResourceGroupRequestValidator.cs",
        "core:Validators/ResourceRequestValidators.cs",
        "core:Validators/ResourceTypeRequestValidators.cs",
        "core:Validators/SchedulingValidators.cs",
        "src:Validators/CreateCalendarFeedRequestValidator.cs",
    };

    [GeneratedRegex(@"(MaximumLength|MinimumLength)\(\d+\)")]
    private static partial Regex BareLengthLimitRegex();

    [Fact]
    public void NoNewValidator_UsesBareLengthLimits()
    {
        BareLengthLimitRegex().IsMatch("RuleFor(x => x.Name).MaximumLength(200);")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => x.Rel.Contains("Validators/", StringComparison.Ordinal))
            .Where(x => BareLengthLimitRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !KnownBareLengthLimitFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "length limits come from DomainLimits, not a bare number (docs/conventions.md). "
            + "Add the constant if it is missing. The grandfathered files are in "
            + "KnownBareLengthLimitFiles and shrink on touch. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void BareLengthLimitBaseline_HasNoStaleEntries()
    {
        var stillOffending = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => x.Rel.Contains("Validators/", StringComparison.Ordinal))
            .Where(x => BareLengthLimitRegex().IsMatch(x.Text))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        var stale = KnownBareLengthLimitFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these validators no longer use a bare length limit — remove them from the "
            + "baseline:\n  " + string.Join("\n  ", stale));
    }

    // ── (i) bare Results.NotFound() outside the calendar feed ────────────────

    /// <summary>
    /// A bare <c>Results.NotFound()</c> (no <c>code</c> in the body) is only for hiding whether
    /// something exists — the anonymous calendar-feed routes. Everything else uses
    /// <c>ErrorResponses.NotFound</c>. These endpoint files predate the rule; fix on touch.
    /// </summary>
    private static readonly HashSet<string> KnownBareNotFoundFiles = new(StringComparer.Ordinal)
    {
        "src:Endpoints/AccountLifecycleEndpoints.cs",
        "src:Endpoints/Ai/AiAllowanceEndpoints.cs",
        "src:Endpoints/Ai/AiConversationEndpoints.cs",
    };

    private static readonly HashSet<string> BareNotFoundExemptFiles = new(StringComparer.Ordinal)
    {
        // Deliberately gives the same answer to unknown, revoked and unentitled.
        "src:Endpoints/CalendarFeedEndpoints.cs",
    };

    [GeneratedRegex(@"Results\.NotFound\(\)")]
    private static partial Regex BareNotFoundRegex();

    [Fact]
    public void NoNewEndpoint_ReturnsABareNotFound()
    {
        BareNotFoundRegex().IsMatch("if (found is null) return Results.NotFound();")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "src"))
            .Where(x => x.Rel.StartsWith("Endpoints/", StringComparison.Ordinal))
            .Where(x => BareNotFoundRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !KnownBareNotFoundFiles.Contains(key) && !BareNotFoundExemptFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "a 404 carries a `code` (ErrorResponses.NotFound / OkOrNotFound / "
            + "NoContentOrNotFound) so the frontend can switch on it; a bare Results.NotFound() "
            + "is only for the anonymous calendar feed. The grandfathered files are in "
            + "KnownBareNotFoundFiles and shrink on touch. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void BareNotFoundBaseline_HasNoStaleEntries()
    {
        var stillOffending = ScanSources(("backend", "src"))
            .Where(x => BareNotFoundRegex().IsMatch(x.Text))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        var stale = KnownBareNotFoundFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these endpoint files no longer return a bare Results.NotFound() — remove them "
            + "from the baseline:\n  " + string.Join("\n  ", stale));
    }

    // ── (j) read verbs ───────────────────────────────────────────────────────

    /// <summary>
    /// Reads are <c>Get</c> (docs/conventions.md, "Naming"). Files that still declare a
    /// <c>Fetch*</c>/<c>Load*</c>/<c>Find*Async</c>; rename on touch.
    /// </summary>
    private static readonly HashSet<string> KnownNonGetReadVerbFiles = new(StringComparer.Ordinal)
    {
        "core:Integrations/Keycloak/KeycloakIdentityLinkService.cs",
        "core:Repositories/AvailabilityEventRepository.cs",
        "core:Repositories/CalendarFeedTokenRepository.cs",
        "core:Repositories/IPlatformUserRepository.cs",
        "core:Repositories/PlatformUserRepository.cs",
        "core:Security/IIdentityLinkService.cs",
        "core:Services/Insights/InsightsService.cs",
        "core:Services/Preset/PresetApplier.cs",
        "core:Services/UserProvisioningService.cs",
    };

    [GeneratedRegex(@"Task(<[^>]*>)?\s+(Fetch|Load|Find)\w*Async\(")]
    private static partial Regex NonGetReadVerbRegex();

    [Fact]
    public void NoNewFile_DeclaresANonGetReadVerb()
    {
        NonGetReadVerbRegex().IsMatch("public async Task<User?> FindByEmailAsync(string email)")
            .Should().BeTrue("the guard regex must match its own exemplar");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => NonGetReadVerbRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !KnownNonGetReadVerbFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "reads are Get* (docs/conventions.md, Naming): not Fetch, Load or Find. The "
            + "grandfathered files are in KnownNonGetReadVerbFiles and shrink on touch. "
            + "Offenders:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NonGetReadVerbBaseline_HasNoStaleEntries()
    {
        var stillOffending = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => NonGetReadVerbRegex().IsMatch(x.Text))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        var stale = KnownNonGetReadVerbFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these files no longer declare a Fetch/Load/Find read — remove them from the "
            + "baseline:\n  " + string.Join("\n  ", stale));
    }
}
