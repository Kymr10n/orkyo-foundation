using Npgsql;

namespace Api.Services;

public readonly record struct TenantReactivationDecisionSnapshot(
    bool IsMember,
    TenantReactivationDecision? Decision,
    string? Role,
    bool IsOwner);

public static class TenantReactivationDecisionFlow
{
    public static TenantReactivationDecisionSnapshot Evaluate(TenantReactivationLookupSnapshot lookup, Guid userId)
    {
        if (!lookup.Found)
        {
            return new TenantReactivationDecisionSnapshot(false, null, null, false);
        }

        var isOwner = lookup.OwnerUserId == userId;
        var decision = TenantReactivationPolicy.Evaluate(
            lookup.Status!,
            lookup.SuspensionReason,
            lookup.Role,
            isOwner);

        return new TenantReactivationDecisionSnapshot(true, decision, lookup.Role, isOwner);
    }

    public static async Task<TenantReactivationDecisionSnapshot> LoadAndEvaluateAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid userId)
    {
        await using var command = TenantReactivationLookupCommandFactory.CreateSelectByTenantAndUserCommand(connection, tenantId, userId);
        await using var reader = await command.ExecuteReaderAsync();
        var lookup = await TenantReactivationLookupReaderFlow.ReadSingleOrNotFoundAsync(reader);
        return Evaluate(lookup, userId);
    }
}
