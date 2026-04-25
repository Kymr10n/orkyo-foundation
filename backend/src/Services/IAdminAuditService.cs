namespace Api.Services;

/// <summary>
/// Records audit events for administrative actions (tenant/user/membership/settings
/// changes). Both products consume the same contract — multi-tenant SaaS writes to a
/// dedicated <c>control_plane.audit_events</c> table; single-tenant Community writes to
/// the same single-DB <c>audit_events</c> table the rest of the deployment shares.
/// Either way the shape is identical: actor, action, target, metadata.
/// </summary>
public interface IAdminAuditService
{
    Task RecordEventAsync(
        Guid? actorUserId,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? metadata = null);
}
