namespace Api.Models;

/// <summary>
/// One resource at a glance, for the status sheet a QR scan opens
/// (docs/qr-resource-linking-spec.md §7). One request, so a phone makes one round trip.
/// </summary>
public record ResourceStatusInfo
{
    public required Guid ResourceId { get; init; }
    public required string Name { get; init; }
    public required string ResourceTypeKey { get; init; }
    public required bool IsActive { get; init; }
    /// <summary>The booking in progress now, if any.</summary>
    public ResourceStatusBooking? Current { get; init; }
    /// <summary>The first booking that starts after now, within the look-ahead window.</summary>
    public ResourceStatusBooking? Next { get; init; }
    /// <summary>An enabled absence that covers now, if any.</summary>
    public ResourceStatusAbsence? ActiveAbsence { get; init; }
    /// <summary>Bookings in the look-ahead window that have a resource-level conflict.</summary>
    public required int ConflictCount { get; init; }
    public required int LookAheadDays { get; init; }
    /// <summary>Average daily allocation over the last <see cref="UtilizationDays"/>, 0–100.</summary>
    public decimal? UtilizationPercent { get; init; }
    public required int UtilizationDays { get; init; }
}

public record ResourceStatusBooking
{
    public required string RequestName { get; init; }
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; init; }
}

public record ResourceStatusAbsence
{
    public required string Title { get; init; }
    public required AbsenceType AbsenceType { get; init; }
    public required DateTime EndTs { get; init; }
}
