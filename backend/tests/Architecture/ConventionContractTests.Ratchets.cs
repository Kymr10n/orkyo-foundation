using System.Text.RegularExpressions;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Ratchets for the conventions docs/conventions.md states but that had no guard until the
/// 2026-09 design review measured them (findings B5 and B6). Same house pattern as the first
/// file: a file-key baseline, a forbid-outside-baseline check, and a reverse staleness check so
/// an entry that stops offending must be removed and the number only goes down. The rows are
/// in <see cref="ConventionRatchets"/>; the first file's theories run them.
/// </summary>
public partial class ConventionContractTests
{
    // ── (f) services that write SQL ──────────────────────────────────────────

    /// <summary>
    /// Services and integrations that reach the database directly — by constructing an
    /// <c>NpgsqlCommand</c>, or by running a query through the <c>NpgsqlQueryExtensions</c> helpers
    /// on their own connection: repositories wearing service names (docs/conventions.md,
    /// "Layering"). Grandfathered; move the query into a repository on touch; new services take a
    /// repository.
    /// </summary>
    private static readonly HashSet<string> KnownSqlWritingServiceFiles = new(StringComparer.Ordinal)
    {
        // Five raw commands on the identity tables; an integration, not a repository.
        "core:Integrations/Keycloak/KeycloakIdentityLinkService.cs",
        "core:Services/AnnouncementBroadcastService.cs",
        "core:Services/Insights/InsightsService.cs",
        "core:Services/InvitationService.cs",
        "core:Services/Preset/PresetApplier.cs",
        "core:Services/PresetService.cs",
        // Reaches the DB through NpgsqlQueryExtensions rather than a raw command.
        "core:Services/Reporting/ReportingQueryService.cs",
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

    private static readonly HashSet<string> SqlWritingServiceExemptFiles = new(StringComparer.Ordinal)
    {
        // "open + SELECT 1" control-plane reachability probe; a raw command by design.
        "core:Services/DbHealthProbe.cs",
    };

    // Both routes from a service to the database: a hand-built command (optionally namespace-
    // qualified) and the NpgsqlQueryExtensions helpers, which take the connection local `conn`
    // or `db` (docs/conventions.md, "Opening a connection").
    [GeneratedRegex(@"new\s+(?:Npgsql\.)?NpgsqlCommand|(?<![\w.])(?:conn|db)\.(?:QueryListAsync|QuerySingleOrDefaultAsync|ExecuteAsync|ExecuteScalarAsync|ExistsAsync|QueryPagedAsync)\(")]
    private static partial Regex ServiceSqlAccessRegex();

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

    // ── (k) ordinal row reads ────────────────────────────────────────────────

    /// <summary>
    /// Rows are read by column name through <c>ReaderExtensions</c>. An ordinal read is
    /// positional: it keeps compiling and starts returning the wrong column the moment a
    /// SELECT list is reordered, and nothing fails until a user sees the wrong value. A JOIN
    /// whose two tables share a column name aliases the duplicate rather than reading by
    /// position. Every grandfathered file was converted, so there is no baseline: the forbid
    /// row guards the whole of <c>backend/src</c> and <c>backend/core</c>.
    /// </summary>

    // Any identifier ending in "reader", not just the two conventional names: a local called
    // `checkReader` held a positional read that this guard could not see for as long as it existed.
    [GeneratedRegex(@"\b(?:[A-Za-z_][A-Za-z0-9_]*)?[Rr]eader\.(?:Get[A-Za-z0-9]+|IsDBNull)\(\d+\)|\br\.(?:Get[A-Za-z0-9]+|IsDBNull)\(\d+\)")]
    private static partial Regex OrdinalRowReadRegex();

    // ── (l) reading the clock directly ───────────────────────────────────────

    /// <summary>
    /// The clock is <c>TimeProvider</c>, registered by <c>AddFoundationServices</c> and
    /// <c>AddFoundationWorkerServices</c>. A direct <c>UtcNow</c> read is untestable: nothing
    /// can move it, so every rule that depends on "now" — expiry, dormancy, the effective
    /// request status — is pinned only at whatever time the suite happens to run. Each file below
    /// has no DI seam (a record, a static helper, a framework-fixed delegate), so it is exempt
    /// rather than grandfathered.
    /// </summary>
    private static readonly HashSet<string> DirectClockExemptFiles = new(StringComparer.Ordinal)
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

    // ── (m) hand-rolled nullable parameter bindings ──────────────────────────

    /// <summary>
    /// A nullable parameter is bound with <c>AddNullable</c> (docs/conventions.md, "Binding a
    /// nullable parameter"), not <c>x ?? DBNull.Value</c> or <c>x.HasValue ? x.Value : DBNull.Value</c>.
    /// Files that still hand-roll the substitution; adopt the helper on touch.
    /// </summary>
    private static readonly HashSet<string> KnownHandRolledNullBindingFiles = new(StringComparer.Ordinal)
    {
        "core:Integrations/Keycloak/KeycloakIdentityLinkService.cs",
        "core:Repositories/AiAllowanceRepository.cs",
        "core:Repositories/AiCredentialRepository.cs",
        "core:Repositories/AssetRepository.cs",
        "core:Repositories/AuditEventWriter.cs",
        "core:Services/Insights/InsightsService.cs",
        "core:Services/Preset/PresetApplier.cs",
        "core:Services/SessionService.cs",
        "core:Services/UserLifecycleService.cs",
        "core:Services/UserSessionService.cs",
    };

    /// <summary>The helpers themselves: <c>AddNullable</c>, <c>AddJsonb</c> and <c>UpdateBuilder</c> do the substitution.</summary>
    private static readonly HashSet<string> HandRolledNullBindingExemptFiles = new(StringComparer.Ordinal)
    {
        "core:Repositories/NpgsqlQueryExtensions.cs",
    };

    // `?? DBNull.Value`, `? DBNull.Value :` and `: DBNull.Value`, each optionally cast to
    // `(object)` — the spellings of the substitution AddNullable owns.
    [GeneratedRegex(@"[?:]\s*(?:\(object\??\)\s*)?DBNull\.Value")]
    private static partial Regex HandRolledNullBindingRegex();

    private static IEnumerable<Ratchet> ConventionRatchets() =>
    [
        new("SqlWritingService", ServiceSqlAccessRegex(), ["core"],
            Scope: f => f.Rel.StartsWith("Services/", StringComparison.Ordinal)
                || f.Rel.StartsWith("Integrations/", StringComparison.Ordinal),
            Baseline: KnownSqlWritingServiceFiles,
            Exempt: SqlWritingServiceExemptFiles,
            Exemplars:
            [
                new("await using var cmd = new NpgsqlCommand(sql, conn);"),
                new("var rows = await conn.QueryListAsync(sql, Map, ct);", true, "the guard regex must match the extension-helper exemplar"),
                new("if (await _repository.ExistsAsync(id, ct))", false, "a repository call is not the service reaching the DB"),
            ],
            ForbidMessage: "services do not write SQL (docs/conventions.md, Layering): put the query in a "
                + "repository and inject it. The grandfathered files are in "
                + "KnownSqlWritingServiceFiles and shrink on touch."),

        new("BareLengthLimit", BareLengthLimitRegex(), ["src", "core"],
            Scope: DeclaresAValidator,
            Baseline: KnownBareLengthLimitFiles,
            Exemplars: [new("RuleFor(x => x.Name).MaximumLength(200);")],
            ForbidMessage: "length limits come from DomainLimits, not a bare number (docs/conventions.md). "
                + "Add the constant if it is missing. The grandfathered files are in "
                + "KnownBareLengthLimitFiles and shrink on touch."),

        new("BareNotFound", BareNotFoundRegex(), ["src"],
            Scope: f => f.Rel.StartsWith("Endpoints/", StringComparison.Ordinal),
            Baseline: KnownBareNotFoundFiles,
            Exempt: BareNotFoundExemptFiles,
            Exemplars: [new("if (found is null) return Results.NotFound();")],
            ForbidMessage: "a 404 carries a `code` (ErrorResponses.NotFound / OkOrNotFound / "
                + "NoContentOrNotFound) so the frontend can switch on it; a bare Results.NotFound() "
                + "is only for the anonymous calendar feed. The grandfathered files are in "
                + "KnownBareNotFoundFiles and shrink on touch."),

        new("OrdinalRowRead", OrdinalRowReadRegex(), ["src", "core"],
            Exemplars:
            [
                new("Id = reader.GetGuid(0),"),
                new("Id = reader.GetGuid(\"id\"),", false, "a name-based read is the rule, not an offence"),
            ],
            ForbidMessage: "rows are read by column name via ReaderExtensions, never by ordinal. Alias the "
                + "column if a JOIN makes the name ambiguous."),

        new("DirectClockRead", DirectClockReadRegex(), ["src", "core"],
            Exempt: DirectClockExemptFiles,
            Exemplars:
            [
                new("var now = DateTime.UtcNow;"),
                new("var now = _time.GetUtcNow();", false, "reading through TimeProvider is the rule, not an offence"),
            ],
            ForbidMessage: "the clock comes from an injected TimeProvider, not DateTime.UtcNow. A "
                + "file with no DI seam goes in DirectClockExemptFiles with its reason."),

        new("HandRolledNullBinding", HandRolledNullBindingRegex(), ["src", "core"],
            Baseline: KnownHandRolledNullBindingFiles,
            Exempt: HandRolledNullBindingExemptFiles,
            Exemplars:
            [
                new("cmd.Parameters.AddWithValue(\"unit\", (object?)unit ?? DBNull.Value);"),
                new("p.AddWithValue(\"scheduled\", scheduled.HasValue ? scheduled.Value : DBNull.Value);"),
                new("p.AddWithValue(\"op\", op is null ? (object)DBNull.Value : op);"),
                new("p.AddNullable(\"unit\", unit);", false, "the helper is the rule, not an offence"),
                new("update.Set(\"site_id\", (object)DBNull.Value);", false, "an explicit NULL is not a nullable binding"),
            ],
            ForbidMessage: "bind a nullable parameter with AddNullable, not `?? DBNull.Value` "
                + "(docs/conventions.md, Data access). The grandfathered files are in "
                + "KnownHandRolledNullBindingFiles and shrink on touch."),
    ];
}
