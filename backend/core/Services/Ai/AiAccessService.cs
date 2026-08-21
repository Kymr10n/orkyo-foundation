using Api.Constants;
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
    Task RecordUsageAsync(Guid userId, long inputTokens, long outputTokens, CancellationToken ct = default);
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
    OrgContext orgContext) : IAiAccessService
{
    public async Task<AiAccessDecision> EvaluateAsync(CancellationToken ct = default)
    {
        if (!await featureGate.IsEnabledAsync(FeatureKeys.AiAssistant, ct))
            return AiAccessDecision.Deny("not_entitled");

        if (!await credentials.IsConfiguredAsync(ct))
            return AiAccessDecision.Deny("not_configured");

        var userId = principal.UserId;

        // Admins always have access, with no budget. Anyone who can replace the workspace's
        // API key cannot be meaningfully constrained by a token ceiling stored beside it.
        if (authorization.IsAdmin)
            return new AiAccessDecision { Allowed = true, MonthlyTokenLimit = null, UsedTotalTokens = 0 };

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

    public Task RecordUsageAsync(Guid userId, long inputTokens, long outputTokens, CancellationToken ct = default) =>
        allowances.RecordUsageAsync(userId, CurrentMonth(), inputTokens, outputTokens, ct);

    /// <summary>
    /// The first day of the current UTC month. This value is the reset: a new month lands
    /// on a new row, so no scheduled job has to run and none can fail to.
    /// </summary>
    private static DateOnly CurrentMonth()
    {
        var now = DateTime.UtcNow;
        return new DateOnly(now.Year, now.Month, 1);
    }
}
