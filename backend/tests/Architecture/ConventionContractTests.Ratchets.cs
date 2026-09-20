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
    /// Services that reach the database directly — by constructing an <c>NpgsqlCommand</c>, or by
    /// running a query through the <c>NpgsqlQueryExtensions</c> helpers on their own connection:
    /// repositories wearing service names (docs/conventions.md, "Layering"). Grandfathered; move
    /// the query into a repository on touch; new services take a repository.
    /// </summary>
    private static readonly HashSet<string> KnownSqlWritingServiceFiles = new(StringComparer.Ordinal)
    {
        "core:Services/AdminAuditService.cs",
        "core:Services/AnnouncementBroadcastService.cs",
        // "open + SELECT 1" control-plane reachability probe; a raw command by design.
        "core:Services/DbHealthProbe.cs",
        "core:Services/Insights/InsightsService.cs",
        "core:Services/InvitationService.cs",
        "core:Services/PlatformApi/ApiAccessTokenService.cs",
        "core:Services/Preset/PresetApplier.cs",
        "core:Services/PresetService.cs",
        // Reaches the DB through NpgsqlQueryExtensions rather than a raw command.
        "core:Services/Reporting/ReportingQueryService.cs",
        "core:Services/Reporting/ReportingTokenService.cs",
        "core:Services/SessionService.cs",
        "core:Services/StarterTemplateService.cs",
        "core:Services/TenantUserService.cs",
        // Writes its lifecycle SQL through the fully-qualified Npgsql.NpgsqlCommand form.
        "core:Services/UserLifecycleService.cs",
        "core:Services/UserManagementService.cs",
        "core:Services/UserProvisioningService.cs",
        "core:Services/UserSessionService.cs",
        "core:Services/WorkerJobCoordinator.cs",
    };

    // Both routes from a service to the database: a hand-built command (optionally namespace-
    // qualified) and the NpgsqlQueryExtensions helpers, which take the connection local `conn`
    // or `db` (docs/conventions.md, "Opening a connection").
    [GeneratedRegex(@"new\s+(?:Npgsql\.)?NpgsqlCommand|(?<![\w.])(?:conn|db)\.(?:QueryListAsync|QuerySingleOrDefaultAsync|ExecuteAsync|ExecuteScalarAsync|ExistsAsync|QueryPagedAsync)\(")]
    private static partial Regex ServiceSqlAccessRegex();

    [Fact]
    public void NoNewService_WritesSql()
    {
        ServiceSqlAccessRegex().IsMatch("await using var cmd = new NpgsqlCommand(sql, conn);")
            .Should().BeTrue("the guard regex must match its own exemplar");
        ServiceSqlAccessRegex().IsMatch("var rows = await conn.QueryListAsync(sql, Map, ct);")
            .Should().BeTrue("the guard regex must match the extension-helper exemplar");
        ServiceSqlAccessRegex().IsMatch("if (await _repository.ExistsAsync(id, ct))")
            .Should().BeFalse("a repository call is not the service reaching the DB");

        var offenders = ScanSources(("backend", "core"))
            .Where(x => x.Rel.StartsWith("Services/", StringComparison.Ordinal))
            .Where(x => ServiceSqlAccessRegex().IsMatch(x.Text))
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
            .Where(x => ServiceSqlAccessRegex().IsMatch(x.Text))
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

    // ── (k) ordinal row reads ────────────────────────────────────────────────

    /// <summary>
    /// Rows are read by column name through <c>ReaderExtensions</c>. An ordinal read is
    /// positional: it keeps compiling and starts returning the wrong column the moment a
    /// SELECT list is reordered, and nothing fails until a user sees the wrong value. A JOIN
    /// whose two tables share a column name aliases the duplicate rather than reading by
    /// position. The baseline is empty — every grandfathered file was converted — so the
    /// forbid fact now guards the whole of <c>backend/src</c> and <c>backend/core</c>.
    /// </summary>
    private static readonly HashSet<string> KnownOrdinalReadFiles = new(StringComparer.Ordinal);

    // Any identifier ending in "reader", not just the two conventional names: a local called
    // `checkReader` held a positional read that this guard could not see for as long as it existed.
    [GeneratedRegex(@"\b(?:[A-Za-z_][A-Za-z0-9_]*)?[Rr]eader\.(?:Get[A-Za-z0-9]+|IsDBNull)\(\d+\)|\br\.(?:Get[A-Za-z0-9]+|IsDBNull)\(\d+\)")]
    private static partial Regex OrdinalRowReadRegex();

    [Fact]
    public void NoNewFile_ReadsARowByOrdinal()
    {
        OrdinalRowReadRegex().IsMatch("Id = reader.GetGuid(0),")
            .Should().BeTrue("the guard regex must match its own exemplar");
        OrdinalRowReadRegex().IsMatch("Id = reader.GetGuid(\"id\"),")
            .Should().BeFalse("a name-based read is the rule, not an offence");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => OrdinalRowReadRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !KnownOrdinalReadFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "rows are read by column name via ReaderExtensions, never by ordinal. Alias the "
            + "column if a JOIN makes the name ambiguous. The grandfathered files are in "
            + "KnownOrdinalReadFiles and shrink on touch. Offenders:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void OrdinalReadBaseline_HasNoStaleEntries()
    {
        var stillOffending = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => OrdinalRowReadRegex().IsMatch(x.Text))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        var stale = KnownOrdinalReadFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these files no longer read a row by ordinal — remove them from the baseline so "
            + "the ratchet moves forward:\n  " + string.Join("\n  ", stale));
    }

    // ── (l) reading the clock directly ───────────────────────────────────────

    /// <summary>
    /// The clock is <c>TimeProvider</c>, registered by <c>AddFoundationServices</c> and
    /// <c>AddFoundationWorkerServices</c>. A direct <c>UtcNow</c> read is untestable: nothing
    /// can move it, so every rule that depends on "now" — expiry, dormancy, the effective
    /// request status — is pinned only at whatever time the suite happens to run. These files
    /// predate the rule and shrink on touch; the reverse staleness fact locks in each
    /// conversion. There is no burn-down schedule.
    /// </summary>
    private static readonly HashSet<string> KnownDirectClockFiles = new(StringComparer.Ordinal)
    {
        // A computed property on a DTO record; nothing injects into a record.
        "core:Models/Announcement.cs",
        // Property initializer and a factory method on plain records; no DI seam.
        "core:Models/Reporting/ReportingModels.cs",
        // Static mapper called from repositories; nothing injects into it.
        "core:Repositories/RequestMapper.cs",
        // Static prompt builder; nothing injects into it.
        "core:Services/Ai/AiSystemPrompt.cs",
        // Static template builders; nothing injects into them.
        "core:Services/EmailTemplates.cs",
        // A computed property on a record base; nothing injects into a record.
        "core:Services/Tokens/TokenRows.cs",
        // Static health-check ResponseWriter delegate with a framework-fixed signature.
        "src:Configuration/FoundationApiHostExtensions.cs",
    };

    [GeneratedRegex(@"DateTime(?:Offset)?\.UtcNow")]
    private static partial Regex DirectClockReadRegex();

    [Fact]
    public void NoNewFile_ReadsTheClockDirectly()
    {
        DirectClockReadRegex().IsMatch("var now = DateTime.UtcNow;")
            .Should().BeTrue("the guard regex must match its own exemplar");
        DirectClockReadRegex().IsMatch("var now = _time.GetUtcNow();")
            .Should().BeFalse("reading through TimeProvider is the rule, not an offence");

        var offenders = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => DirectClockReadRegex().IsMatch(x.Text))
            .Select(x => x.Key)
            .Where(key => !KnownDirectClockFiles.Contains(key))
            .ToList();

        offenders.Should().BeEmpty(
            "the clock comes from an injected TimeProvider, not DateTime.UtcNow. The "
            + "grandfathered files are in KnownDirectClockFiles and shrink on touch. "
            + "Offenders:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void DirectClockBaseline_HasNoStaleEntries()
    {
        var stillOffending = ScanSources(("backend", "src"), ("backend", "core"))
            .Where(x => DirectClockReadRegex().IsMatch(x.Text))
            .Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        var stale = KnownDirectClockFiles.Where(f => !stillOffending.Contains(f)).ToList();

        stale.Should().BeEmpty(
            "these files no longer read the clock directly — remove them from the baseline so "
            + "the ratchet moves forward:\n  " + string.Join("\n  ", stale));
    }
}
