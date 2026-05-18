using Npgsql;

namespace Api.Services;

public static class TenantOwnerStatusDecisionFlow
{
    public static TenantDeleteDecision EvaluateDelete(
        TenantOwnerStatusSnapshot ownerStatus,
        Guid actorUserId,
        bool isSiteAdmin)
    {
        return TenantDeletionPolicy.EvaluateDelete(
            ownerStatus.Found,
            ownerStatus.OwnerId,
            actorUserId,
            isSiteAdmin,
            ownerStatus.Status);
    }

    public static TenantCancelDeletionDecision EvaluateCancelDeletion(
        TenantOwnerStatusSnapshot ownerStatus,
        Guid actorUserId,
        bool isSiteAdmin)
    {
        return TenantDeletionPolicy.EvaluateCancelDeletion(
            ownerStatus.Found,
            ownerStatus.OwnerId,
            actorUserId,
            isSiteAdmin,
            ownerStatus.Status);
    }

    public static TenantUpdateDecision EvaluateUpdate(
        TenantOwnerStatusSnapshot ownerStatus,
        Guid actorUserId,
        bool isSiteAdmin,
        string? displayName)
    {
        return TenantUpdatePolicy.Evaluate(
            ownerStatus.Found,
            ownerStatus.OwnerId,
            actorUserId,
            isSiteAdmin,
            ownerStatus.Status,
            displayName);
    }

    public static async Task<TenantDeleteDecision> LoadAndEvaluateDeleteAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid actorUserId,
        bool isSiteAdmin)
    {
        var ownerStatus = await LoadOwnerStatusAsync(connection, tenantId);
        return EvaluateDelete(ownerStatus, actorUserId, isSiteAdmin);
    }

    public static async Task<TenantCancelDeletionDecision> LoadAndEvaluateCancelDeletionAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid actorUserId,
        bool isSiteAdmin)
    {
        var ownerStatus = await LoadOwnerStatusAsync(connection, tenantId);
        return EvaluateCancelDeletion(ownerStatus, actorUserId, isSiteAdmin);
    }

    public static async Task<TenantUpdateDecision> LoadAndEvaluateUpdateAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        Guid actorUserId,
        bool isSiteAdmin,
        string? displayName)
    {
        var ownerStatus = await LoadOwnerStatusAsync(connection, tenantId);
        return EvaluateUpdate(ownerStatus, actorUserId, isSiteAdmin, displayName);
    }

    private static async Task<TenantOwnerStatusSnapshot> LoadOwnerStatusAsync(NpgsqlConnection connection, Guid tenantId)
    {
        await using var command = TenantOwnerStatusCommandFactory.CreateSelectByTenantIdCommand(connection, tenantId);
        await using var reader = await command.ExecuteReaderAsync();
        return await TenantOwnerStatusReaderFlow.ReadSingleOrNotFoundAsync(reader);
    }
}
