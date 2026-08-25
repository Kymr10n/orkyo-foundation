using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Security.Features;

namespace Api.Services.Ai;

public interface IAiAccessService
{
    /// <summary>
    /// Whether the caller may run a turn right now, and what budget they have left.
    /// Checks, in order: the workspace entitlement, a stored credential, the caller's
    /// grant, and this month's spend.
    /// </summary>
    Task<AiAccessDecision> EvaluateAsync(CancellationToken ct = default);

    /// <summary>What the member-facing status endpoint reports.</summary>
    Task<AiStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Every workspace member with their grant and this month's spend, for the admin table.</summary>
    Task<IReadOnlyList<AiUserAllowance>> ListAllowancesAsync(CancellationToken ct = default);

    Task SetAllowanceAsync(Guid userId, long? monthlyTokenLimit, Guid? actorUserId, CancellationToken ct = default);

    Task<bool> RevokeAllowanceAsync(Guid userId, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Adds one turn's token spend to the caller's month.</summary>
    Task RecordUsageAsync(long inputTokens, long outputTokens, CancellationToken ct = default);

    /// <summary>
    /// Counts one interaction against the caller's day. Called when a turn starts, so a
    /// turn that fails still counts — the limit is on what the workspace allows people to
    /// ask for, not on what happened to succeed.
    /// </summary>
    Task RecordDailyTurnAsync(CancellationToken ct = default);

    /// <summary>The workspace's daily interaction limits, for the admin tab.</summary>
    Task<AiDailyLimits> GetDailyLimitsAsync(CancellationToken ct = default);

    /// <summary>
    /// Replaces the workspace's daily interaction limits. Null clears a limit; values
    /// must be positive — "zero interactions" is what removing access is for.
    /// </summary>
    Task SetDailyLimitsAsync(int? userDailyTurns, int? tenantDailyTurns, Guid? actorUserId, CancellationToken ct = default);
}

/// <summary>
/// Decides who may use the assistant and how much they may spend.
///
/// The default is deliberately restrictive: a member with no allowance row cannot use
/// the assistant at all. Tenant admins bypass the table, which is what makes the
/// workspace usable the moment a key is saved — the admin who configured it can try it
/// immediately, and then grants budgets to the people who need one.
/// </summary>
public sealed class AiAccessService(
    IAiAllowanceRepository allowances,
    IAiCredentialService credentials,
    IFeatureGate featureGate,
    IAuthorizationContext authorization,
    ICurrentPrincipal principal,
    ITenantUserService tenantUserService,
    IAccountMutationGuard accountGuard,
    OrgContext orgContext) : IAiAccessService
{
    public async Task<AiAccessDecision> EvaluateAsync(CancellationToken ct = default)
    {
        if (!await featureGate.IsEnabledAsync(FeatureKeys.AiAssistant, ct))
            return AiAccessDecision.Deny("not_entitled");

        if (!await credentials.IsConfiguredAsync(ct))
            return AiAccessDecision.Deny("not_configured");

        var userId = principal.UserId;

        // The daily interaction limit applies to everyone the workspace has it set for,
        // administrators included: unlike the monthly token ceiling, this one exists to cap
        // what a single person spends in a day, and an exception for the people most likely
        // to be exploring it would defeat it.
        var shared = accountGuard.IsAccountLocked(principal);
        var daily = await ReadDailyAsync(SubjectFor(shared), ct);
        if (daily.Exhausted)
        {
            return new AiAccessDecision
            {
                Allowed = false,
                Reason = daily.IsWorkspaceLimit ? "workspace_daily_limit_reached" : "daily_limit_reached",
                DailyTurnLimit = daily.Limit,
                UsedTurnsToday = daily.Used,
                DailyLimitIsWorkspaceWide = daily.IsWorkspaceLimit,
            };
        }

        // Admins always have access, with no monthly budget. Anyone who can replace the
        // workspace's API key cannot be meaningfully constrained by a token ceiling stored
        // beside it — but a shared login is many people behind one password, and that
        // reasoning does not survive it.
        if (authorization.IsAdmin && !shared)
        {
            return new AiAccessDecision
            {
                Allowed = true,
                MonthlyTokenLimit = null,
                UsedTotalTokens = 0,
                DailyTurnLimit = daily.Limit,
                UsedTurnsToday = daily.Used,
                DailyLimitIsWorkspaceWide = daily.IsWorkspaceLimit,
            };
        }

        var allowance = await allowances.GetAllowanceAsync(userId, ct);
        if (allowance is null)
            return AiAccessDecision.Deny("not_allowed");

        var used = await allowances.GetUsageAsync(userId, CurrentMonth(), ct);
        var usedTotal = used?.TotalTokens ?? 0;

        // Null limit means unlimited. Zero means the admin kept the grant but stopped the spend.
        if (allowance.MonthlyTokenLimit is { } limit && usedTotal >= limit)
        {
            return new AiAccessDecision
            {
                Allowed = false,
                Reason = "allowance_exhausted",
                MonthlyTokenLimit = limit,
                UsedTotalTokens = usedTotal,
            };
        }

        return new AiAccessDecision
        {
            Allowed = true,
            MonthlyTokenLimit = allowance.MonthlyTokenLimit,
            UsedTotalTokens = usedTotal,
            // Carried on the allowed path too: this is what the panel counts down from.
            DailyTurnLimit = daily.Limit,
            UsedTurnsToday = daily.Used,
            DailyLimitIsWorkspaceWide = daily.IsWorkspaceLimit,
        };
    }

    public async Task<AiStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var decision = await EvaluateAsync(ct);
        return new AiStatus
        {
            Available = decision.Allowed,
            Reason = decision.Reason,
            MonthlyTokenLimit = decision.MonthlyTokenLimit,
            UsedTotalTokens = decision.UsedTotalTokens,
            DailyTurnLimit = decision.DailyTurnLimit,
            UsedTurnsToday = decision.UsedTurnsToday,
            DailyLimitIsWorkspaceWide = decision.DailyLimitIsWorkspaceWide,
        };
    }

    public Task<IReadOnlyList<AiUserAllowance>> ListAllowancesAsync(CancellationToken ct = default) =>
        allowances.ListMemberAllowancesAsync(CurrentMonth(), ct);

    public async Task SetAllowanceAsync(Guid userId, long? monthlyTokenLimit, Guid? actorUserId, CancellationToken ct = default)
    {
        if (monthlyTokenLimit is < 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyTokenLimit), "A token limit cannot be negative.");

        await allowances.UpsertAllowanceAsync(userId, monthlyTokenLimit, actorUserId, ct);

        await tenantUserService.RecordAuditEventAsync(
            orgContext, TenantAuditActions.AiAllowanceGranted, actorUserId,
            targetType: "user", targetId: userId.ToString(),
            metadata: new { monthlyTokenLimit }, ct: ct);
    }

    public async Task<bool> RevokeAllowanceAsync(Guid userId, Guid? actorUserId, CancellationToken ct = default)
    {
        var removed = await allowances.DeleteAllowanceAsync(userId, ct);
        if (!removed) return false;

        await tenantUserService.RecordAuditEventAsync(
            orgContext, TenantAuditActions.AiAllowanceRevoked, actorUserId,
            targetType: "user", targetId: userId.ToString(), ct: ct);

        return true;
    }

    public Task<AiDailyLimits> GetDailyLimitsAsync(CancellationToken ct = default) =>
        allowances.GetDailyLimitsAsync(ct);

    public async Task SetDailyLimitsAsync(int? userDailyTurns, int? tenantDailyTurns, Guid? actorUserId, CancellationToken ct = default)
    {
        // A shared login is many people behind one password, so nobody behind it may raise
        // the ceiling that governs them all. Today the route also requires the admin role
        // and the demo membership is seeded as an editor — but that is one string in a
        // migration, and the limit this protects is spend on the workspace's own API key.
        if (accountGuard.IsAccountLocked(principal))
            throw new AccountLockedException("A shared account cannot change the daily limits.");

        if (userDailyTurns is < 1)
            throw new ArgumentOutOfRangeException(nameof(userDailyTurns), "A daily limit must be positive; clear it for no limit.");
        if (tenantDailyTurns is < 1)
            throw new ArgumentOutOfRangeException(nameof(tenantDailyTurns), "A daily limit must be positive; clear it for no limit.");

        await allowances.SetDailyLimitsAsync(userDailyTurns, tenantDailyTurns, actorUserId, ct);

        await tenantUserService.RecordAuditEventAsync(
            orgContext, TenantAuditActions.AiDailyLimitsChanged, actorUserId,
            targetType: "workspace", targetId: null,
            metadata: new { userDailyTurns, tenantDailyTurns }, ct: ct);
    }

    public Task RecordDailyTurnAsync(CancellationToken ct = default) =>
        allowances.RecordDailyTurnAsync(SubjectFor(accountGuard.IsAccountLocked(principal)), CurrentDay(), ct);

    public Task RecordUsageAsync(long inputTokens, long outputTokens, CancellationToken ct = default) =>
        // The subject comes from the principal, like every other method here. It used to
        // be a parameter, which made billing the wrong person's allowance a typo away.
        allowances.RecordUsageAsync(principal.UserId, CurrentMonth(), inputTokens, outputTokens, ct);

    /// <summary>
    /// Who the daily count belongs to. A shared login has one user id for everybody, so the
    /// identity provider's session id is the only handle that separates two visitors. When
    /// a shared session carries no session id there is nothing to separate them by, and the
    /// safe reading is "one bucket for all of them" rather than "no limit".
    /// </summary>
    private string SubjectFor(bool shared) =>
        shared ? principal.SessionId ?? "shared-session-unknown" : principal.UserId.ToString();

    /// <summary>
    /// How the caller stands against whichever daily limit binds them soonest.
    ///
    /// Reporting the binding one matters: a member with 12 personal turns left can still be
    /// stopped by the workspace total, and counting down a number that is not the one about
    /// to stop you is worse than showing nothing.
    /// </summary>
    private sealed record DailyState(int? Limit, int Used, bool Exhausted, bool IsWorkspaceLimit)
    {
        public static readonly DailyState Unlimited = new(null, 0, false, false);

        /// <summary>Turns left, or int.MaxValue when there is no limit so "none" sorts last.</summary>
        public int Remaining => Limit is { } limit ? Math.Max(0, limit - Used) : int.MaxValue;
    }

    /// <summary>
    /// Reads the workspace's daily interaction limits and what has been used against them.
    /// The limits are workspace settings a workspace administrator maintains in the
    /// AI Assistant tab, beside the token allowances; most workspaces set neither.
    /// </summary>
    private async Task<DailyState> ReadDailyAsync(string subject, CancellationToken ct)
    {
        var limits = await allowances.GetDailyLimitsAsync(ct);
        if (limits.UserDailyTurns is null && limits.TenantDailyTurns is null)
            return DailyState.Unlimited;

        var day = CurrentDay();
        DailyState state = DailyState.Unlimited;

        if (limits.UserDailyTurns is { } perUser)
        {
            var mine = await allowances.GetDailyTurnsAsync(subject, day, ct);
            state = new DailyState(perUser, mine, mine >= perUser, IsWorkspaceLimit: false);
        }

        // Both limits are evaluated every time, and the one with less headroom is what the
        // caller is told about. A workspace limit that only appeared at the moment it bit
        // gave no warning at all, which is the opposite of what a limit is for.
        if (limits.TenantDailyTurns is { } perTenant)
        {
            var everyone = await allowances.GetTenantDailyTurnsAsync(day, ct);
            var workspace = new DailyState(perTenant, everyone, everyone >= perTenant, IsWorkspaceLimit: true);
            if (workspace.Remaining < state.Remaining) state = workspace;
        }

        return state;
    }

    /// <summary>
    /// Today, in UTC. This value is the reset: a new day lands on a new row, so no job has
    /// to run and none can fail to — the same reasoning as <see cref="CurrentMonth"/>.
    /// </summary>
    private static DateOnly CurrentDay() => DateOnly.FromDateTime(DateTime.UtcNow);

    /// The first day of the current UTC month. This value is the reset: a new month lands
    /// on a new row, so no scheduled job has to run and none can fail to.
    /// </summary>
    private static DateOnly CurrentMonth()
    {
        var now = DateTime.UtcNow;
        return new DateOnly(now.Year, now.Month, 1);
    }
}
