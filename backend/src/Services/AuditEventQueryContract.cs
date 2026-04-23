namespace Api.Services;

/// <summary>
/// Filter for the platform <c>audit_events</c> list query.
/// All members are optional; a fully-null filter selects all rows.
/// </summary>
public sealed record AuditEventListFilter(
    string? Action,
    Guid? ActorUserId,
    string? TargetType,
    string? TargetId,
    DateTime? FromUtc,
    DateTime? ToUtc);

/// <summary>
/// Read-side projection of an <c>audit_events</c> row matching
/// <see cref="AuditEventQueryContract.SelectColumns"/>.
/// JSONB <c>metadata</c> is materialized as its <c>::text</c> form.
/// </summary>
public sealed record AuditEventReadProjection(
    Guid Id,
    Guid? ActorUserId,
    string ActorType,
    string Action,
    string? TargetType,
    string? TargetId,
    string? Metadata,
    string? RequestId,
    string? IpAddress,
    DateTime CreatedAt);

/// <summary>
/// SQL contract for paginated platform-wide audit event reads.
///
/// The <c>audit_events</c> table layout is structurally identical in
/// multi-tenant SaaS and single-tenant Community deployments, so the
/// SELECT-column list, COUNT/SELECT shapes, and filter binding belong
/// in foundation by default.
/// </summary>
public static class AuditEventQueryContract
{
    public const string ActionParameterName = "action";
    public const string ActorIdParameterName = "actorId";
    public const string TargetTypeParameterName = "targetType";
    public const string TargetIdParameterName = "targetId";
    public const string FromParameterName = "from";
    public const string ToParameterName = "to";
    public const string PageSizeParameterName = "pageSize";
    public const string OffsetParameterName = "offset";

    /// <summary>
    /// Canonical SELECT column list for <c>audit_events</c> reads. Reader
    /// projection in <see cref="AuditEventReaderFlow.ReadEventsAsync"/> reads
    /// these columns positionally; keep both in sync.
    /// </summary>
    public const string SelectColumns =
        "id, actor_user_id, actor_type, action, target_type, target_id, " +
        "metadata::text, request_id, ip_address, created_at";

    public static string BuildCountSql(string whereClause)
    {
        return string.IsNullOrEmpty(whereClause)
            ? "SELECT COUNT(*) FROM audit_events"
            : $"SELECT COUNT(*) FROM audit_events {whereClause}";
    }

    public static string BuildSelectPageSql(string whereClause)
    {
        var prefix = string.IsNullOrEmpty(whereClause) ? string.Empty : whereClause + "\n            ";
        return $@"
            SELECT {SelectColumns}
            FROM audit_events
            {prefix}ORDER BY created_at DESC
            LIMIT @pageSize OFFSET @offset";
    }
}
