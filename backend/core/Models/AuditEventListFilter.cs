namespace Api.Models;

/// <summary>Filter for an audit_events list query. All members optional.</summary>
public sealed record AuditEventListFilter(
    string? Action,
    Guid? ActorUserId,
    string? TargetType,
    string? TargetId,
    DateTime? FromUtc,
    DateTime? ToUtc);
