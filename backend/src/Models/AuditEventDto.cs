namespace Api.Models;

/// <summary>
/// API projection of an <c>audit_events</c> row. Both multi-tenant SaaS and
/// single-tenant Community surface audit events through the same shape —
/// the underlying table schema is identical and the read surface is
/// product-agnostic.
/// </summary>
public record AuditEventDto
{
    public required Guid Id { get; init; }
    public Guid? ActorUserId { get; init; }
    public required string ActorType { get; init; }
    public required string Action { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? Metadata { get; init; }
    public string? RequestId { get; init; }
    public string? IpAddress { get; init; }
    public required DateTime CreatedAt { get; init; }
}
