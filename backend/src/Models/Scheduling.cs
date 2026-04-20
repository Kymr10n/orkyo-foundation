using System.Text.Json.Serialization;

namespace Api.Models;

/// <summary>
/// Off-time type classification.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OffTimeType
{
    [JsonStringEnumMemberName("holiday")]
    Holiday,

    [JsonStringEnumMemberName("maintenance")]
    Maintenance,

    [JsonStringEnumMemberName("custom")]
    Custom
}

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

    /// <summary>
    /// Returns sensible defaults (all scheduling features disabled).
    /// The application behaves as if scheduling was never configured.
    /// </summary>
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

/// <summary>
/// Request to create or update scheduling settings for a site.
/// </summary>
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

/// <summary>
/// Off-time entry (holiday, maintenance window, or custom off period).
/// </summary>
public record OffTimeInfo
{
    public required Guid Id { get; init; }
    public required Guid SiteId { get; init; }
    public required string Title { get; init; }
    public required OffTimeType Type { get; init; }
    public required bool AppliesToAllSpaces { get; init; }
    public List<Guid>? SpaceIds { get; init; }
    public required DateTime StartTs { get; init; }
    public required DateTime EndTs { get; init; }
    public required bool IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public required bool Enabled { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request to create an off-time entry.
/// </summary>
public record CreateOffTimeRequest
{
    public required string Title { get; init; }
    public OffTimeType Type { get; init; } = OffTimeType.Custom;
    public bool AppliesToAllSpaces { get; init; } = true;
    public List<Guid>? SpaceIds { get; init; }
    public required DateTime StartTs { get; init; }
    public required DateTime EndTs { get; init; }
    public bool IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Request to update an existing off-time entry.
/// </summary>
public record UpdateOffTimeRequest
{
    public string? Title { get; init; }
    public OffTimeType? Type { get; init; }
    public bool? AppliesToAllSpaces { get; init; }
    public List<Guid>? SpaceIds { get; init; }
    public DateTime? StartTs { get; init; }
    public DateTime? EndTs { get; init; }
    public bool? IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public bool? Enabled { get; init; }
}
