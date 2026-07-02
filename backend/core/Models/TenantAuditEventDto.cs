namespace Api.Models;

/// <summary>
/// Tenant-facing projection of an <c>audit_events</c> row for the tenant-admin audit log
/// (<c>GET /api/audit</c>). Deliberately omits the sensitive <c>ip_address</c> and
/// <c>request_id</c> fields that the site-admin <see cref="AuditEventDto"/> exposes, and
/// resolves the actor's email/display name for display.
/// </summary>
public record TenantAuditEventDto
{
    public required Guid Id { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ActorEmail { get; init; }
    public string? ActorDisplayName { get; init; }
    public required string ActorType { get; init; }
    public required string Action { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? Metadata { get; init; }
    public required DateTime CreatedAt { get; init; }
}
