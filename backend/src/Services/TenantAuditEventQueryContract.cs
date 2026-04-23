namespace Api.Services;

/// <summary>
/// SQL + actor-type contract for inserting an audit event into a tenant
/// database's <c>audit_events</c> table.
///
/// Actor-type semantics: when an actor user-id is present the actor type is
/// <c>"user"</c>; otherwise it is <c>"system"</c>. The metadata column is JSONB.
/// </summary>
public static class TenantAuditEventQueryContract
{
    public const string ActorUserIdParameterName = "actorUserId";
    public const string ActorTypeParameterName = "actorType";
    public const string ActionParameterName = "action";
    public const string TargetTypeParameterName = "targetType";
    public const string TargetIdParameterName = "targetId";
    public const string MetadataParameterName = "metadata";

    public const string ActorTypeUser = "user";
    public const string ActorTypeSystem = "system";

    /// <summary>
    /// Resolve the actor type for an audit-event insert based on whether an
    /// actor user-id is present.
    /// </summary>
    public static string ResolveActorType(Guid? actorUserId)
        => actorUserId.HasValue ? ActorTypeUser : ActorTypeSystem;

    public static string BuildInsertAuditEventSql()
    {
        return @"
            INSERT INTO audit_events (actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)
            VALUES (@actorUserId, @actorType, @action, @targetType, @targetId, @metadata, NOW())
        ";
    }
}
