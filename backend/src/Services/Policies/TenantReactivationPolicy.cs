using Api.Constants;
using Orkyo.Shared;

namespace Api.Services;

public enum TenantReactivationDecision
{
    Allowed,
    NotSuspended,
    NotSelfService,
    NotAuthorized
}

public static class TenantReactivationPolicy
{
    public static TenantReactivationDecision Evaluate(string status, string? reason, string? role, bool isOwner)
    {
        if (!string.Equals(status, TenantStatusConstants.Suspended, StringComparison.OrdinalIgnoreCase))
            return TenantReactivationDecision.NotSuspended;

        if (!SuspensionPolicy.IsSelfServiceReason(reason))
            return TenantReactivationDecision.NotSelfService;

        if (!isOwner && !string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase))
            return TenantReactivationDecision.NotAuthorized;

        return TenantReactivationDecision.Allowed;
    }
}
