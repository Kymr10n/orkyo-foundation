namespace Api.Services;

/// <summary>
/// SQL + actor-type contract for inserting an audit event into the control-plane
/// <c>audit_events</c> table.
///
/// Differs from the tenant-database audit-event contract in that the control-plane
/// table requires an explicit <c>id</c> column; tenant audit-event rows rely on a
/// database default for the primary key. Actor-type semantics are shared:
/// when an actor user-id is present the actor type is <c>"user"</c>; otherwise
/// <c>"system"</c>. Metadata column is JSONB.
/// </summary>
public static class ControlPlaneAuditEventQueryContract
{
    public const string IdParameterName = "id";
    public const string ActorUserIdParameterName = "actorUserId";
    public const string ActorTypeParameterName = "actorType";
    public const string ActionParameterName = "action";
    public const string TargetTypeParameterName = "targetType";
    public const string TargetIdParameterName = "targetId";
    public const string MetadataParameterName = "metadata";

    public const string ActorTypeUser = TenantAuditEventQueryContract.ActorTypeUser;
    public const string ActorTypeSystem = TenantAuditEventQueryContract.ActorTypeSystem;

    /// <summary>
    /// Resolve the actor type for a control-plane audit-event insert based on
    /// whether an actor user-id is present. Delegates to the shared tenant
    /// audit-event resolver to keep semantics aligned across audit contracts.
    /// </summary>
    public static string ResolveActorType(Guid? actorUserId)
        => TenantAuditEventQueryContract.ResolveActorType(actorUserId);

    public static string BuildInsertAuditEventSql()
    {
        return @"
            INSERT INTO audit_events (id, actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)
            VALUES (@id, @actorUserId, @actorType, @action, @targetType, @targetId, @metadata, NOW())
        ";
    }
}
