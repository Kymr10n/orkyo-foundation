namespace Api.Models;

/// <summary>
/// One conflict on a request, shaped to match the frontend `Conflict` type so the conflicts
/// registry can be consumed directly. <see cref="Kind"/> / <see cref="Severity"/> are the FE
/// string unions (e.g. "connector_mismatch", "overlap", "capacity_exceeded", "starts_in_off_time",
/// "below_min_duration", "before_earliest_start", "after_latest_end"; "warning" | "error").
/// </summary>
public record ConflictInfo
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    /// <summary>For `overlap` conflicts: the peer request this one overlaps with.</summary>
    public Guid? PeerRequestId { get; init; }
    /// <summary>The assigned resource (space/person/tool) this conflict is about, when it maps to one
    /// — lets the editor flag the specific row. Null for request-level conflicts (e.g. timing).</summary>
    public Guid? ResourceId { get; init; }
    /// <summary>For capability conflicts: the unmet requirement's criterion — lets the editor flag the
    /// specific requirement row. Null otherwise.</summary>
    public Guid? CriterionId { get; init; }
}

/// <summary>All conflicts for one request — the unit returned by the conflicts registry.</summary>
public record RequestConflictInfo
{
    public required Guid RequestId { get; init; }
    public required IReadOnlyList<ConflictInfo> Conflicts { get; init; }
}
