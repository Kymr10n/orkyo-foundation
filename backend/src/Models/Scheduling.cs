using System.Text.Json.Serialization;
using Api.Helpers;

namespace Api.Models;

// ── Scheduling Settings ──────────────────────────────────────────────────────

/// <summary>
/// Scheduling settings for a site (one per site).
/// Controls working hours, weekends, and public holiday handling.
/// </summary>
public record SchedulingSettingsInfo
{
    public required Guid Id { get; init; }
    public required Guid SiteId { get; init; }
    public required string TimeZone { get; init; }
    public required bool WorkingHoursEnabled { get; init; }
    public required TimeOnly WorkingDayStart { get; init; }
    public required TimeOnly WorkingDayEnd { get; init; }
    public required bool WeekendsEnabled { get; init; }
    public required bool PublicHolidaysEnabled { get; init; }
    public string? PublicHolidayRegion { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static SchedulingSettingsInfo Default(Guid siteId) => new()
    {
        Id = Guid.Empty,
        SiteId = siteId,
        TimeZone = "UTC",
        WorkingHoursEnabled = false,
        WorkingDayStart = new TimeOnly(8, 0),
        WorkingDayEnd = new TimeOnly(17, 0),
        WeekendsEnabled = true,
        PublicHolidaysEnabled = false
    };
}

public record UpsertSchedulingSettingsRequest
{
    public string TimeZone { get; init; } = "UTC";
    public bool WorkingHoursEnabled { get; init; }
    public string WorkingDayStart { get; init; } = "08:00";
    public string WorkingDayEnd { get; init; } = "17:00";
    public bool WeekendsEnabled { get; init; } = true;
    public bool PublicHolidaysEnabled { get; init; }
    public string? PublicHolidayRegion { get; init; }
}

// ── Availability Events ──────────────────────────────────────────────────────

[JsonConverter(typeof(DbMappedEnumConverter<AvailabilityEventType>))]
public enum AvailabilityEventType
{
    [JsonStringEnumMemberName("public_holiday")]
    PublicHoliday,

    [JsonStringEnumMemberName("shutdown")]
    Shutdown,

    [JsonStringEnumMemberName("maintenance")]
    Maintenance,

    [JsonStringEnumMemberName("custom")]
    Custom
}

[JsonConverter(typeof(DbMappedEnumConverter<DefaultEffect>))]
public enum DefaultEffect
{
    [JsonStringEnumMemberName("closed")]
    Closed,

    [JsonStringEnumMemberName("available")]
    Available
}

[JsonConverter(typeof(DbMappedEnumConverter<ScopeEffect>))]
public enum ScopeEffect
{
    [JsonStringEnumMemberName("available")]
    Available,

    [JsonStringEnumMemberName("closed")]
    Closed
}

[JsonConverter(typeof(DbMappedEnumConverter<ScopeTargetType>))]
public enum ScopeTargetType
{
    [JsonStringEnumMemberName("resource")]
    Resource,

    [JsonStringEnumMemberName("resource_group")]
    ResourceGroup,

    [JsonStringEnumMemberName("resource_type")]
    ResourceType
}

public record AvailabilityEventScopeInfo
{
    public required Guid Id { get; init; }
    public required Guid AvailabilityEventId { get; init; }
    public required ScopeTargetType TargetType { get; init; }
    public required Guid TargetId { get; init; }
    public required ScopeEffect Effect { get; init; }
}

public record AvailabilityEventInfo
{
    public required Guid Id { get; init; }
    public required Guid SiteId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required AvailabilityEventType EventType { get; init; }
    public required DefaultEffect DefaultEffect { get; init; }
    public required DateTime StartTs { get; init; }
    public required DateTime EndTs { get; init; }
    public required bool IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public required bool Enabled { get; init; }
    public List<AvailabilityEventScopeInfo> Scopes { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record AddScopeRequest
{
    public required ScopeTargetType TargetType { get; init; }
    public required Guid TargetId { get; init; }
    public required ScopeEffect Effect { get; init; }
}

public record UpdateScopeRequest
{
    public ScopeEffect? Effect { get; init; }
}

public record CreateAvailabilityEventRequest
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public AvailabilityEventType EventType { get; init; } = AvailabilityEventType.Custom;
    public DefaultEffect DefaultEffect { get; init; } = DefaultEffect.Closed;
    public required DateTime StartTs { get; init; }
    public required DateTime EndTs { get; init; }
    public bool IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public bool Enabled { get; init; } = true;
    public List<AddScopeRequest> Scopes { get; init; } = [];
}

public record UpdateAvailabilityEventRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public AvailabilityEventType? EventType { get; init; }
    public DefaultEffect? DefaultEffect { get; init; }
    public DateTime? StartTs { get; init; }
    public DateTime? EndTs { get; init; }
    public bool? IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public bool? Enabled { get; init; }
}

// ── Resource Absences ────────────────────────────────────────────────────────

[JsonConverter(typeof(DbMappedEnumConverter<AbsenceType>))]
public enum AbsenceType
{
    [JsonStringEnumMemberName("vacation")]
    Vacation,

    [JsonStringEnumMemberName("sickness")]
    Sickness,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable,

    [JsonStringEnumMemberName("training")]
    Training,

    [JsonStringEnumMemberName("maintenance")]
    Maintenance,

    [JsonStringEnumMemberName("custom")]
    Custom
}

public record ResourceAbsenceInfo
{
    public required Guid Id { get; init; }
    public required Guid ResourceId { get; init; }
    public required AbsenceType AbsenceType { get; init; }
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public required DateTime StartTs { get; init; }
    public required DateTime EndTs { get; init; }
    public required bool IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public required bool Enabled { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateResourceAbsenceRequest
{
    public required AbsenceType AbsenceType { get; init; }
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public required DateTime StartTs { get; init; }
    public required DateTime EndTs { get; init; }
    public bool IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public bool Enabled { get; init; } = true;
}

public record UpdateResourceAbsenceRequest
{
    public AbsenceType? AbsenceType { get; init; }
    public string? Title { get; init; }
    public string? Notes { get; init; }
    public DateTime? StartTs { get; init; }
    public DateTime? EndTs { get; init; }
    public bool? IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public bool? Enabled { get; init; }
}

// ── Engine-facing type ───────────────────────────────────────────────────────

/// <summary>
/// A resolved unavailability window for a resource. Produced by
/// <see cref="IAvailabilityResolver"/> as the union of resource absences
/// and closing availability events. Consumed by the scheduling engine and validator.
/// </summary>
public enum BlockedPeriodSource
{
    AvailabilityEvent,
    ResourceAbsence
}

public record BlockedPeriod
{
    public required Guid Id { get; init; }
    public required DateTime StartTs { get; init; }
    public required DateTime EndTs { get; init; }
    public required string Title { get; init; }
    public required BlockedPeriodSource Source { get; init; }
    /// <summary>Set when Source == AvailabilityEvent. Value matches the DB event_type string.</summary>
    public string? EventType { get; init; }
    /// <summary>Set when Source == ResourceAbsence. Value matches the DB absence_type string.</summary>
    public string? AbsenceType { get; init; }
}
