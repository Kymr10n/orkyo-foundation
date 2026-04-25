using System.Text.Json.Serialization;
using Api.Models;

namespace Api.Models.Export;

public record ExportRequest
{
    public List<Guid>? SiteIds { get; init; }
    public bool IncludeMasterData { get; init; } = true;
    public bool IncludePlanningData { get; init; } = false;
}

public record ExportPayload
{
    public required string SchemaVersion { get; init; }
    public required ExportProvenance Provenance { get; init; }
    public required ExportData Data { get; init; }
}

public record ExportProvenance
{
    public required DateTime ExportTimestamp { get; init; }
    public required string TenantSlug { get; init; }
    public List<Guid>? SiteIds { get; init; }
    public required string SchemaVersion { get; init; }
}

public record ExportData
{
    public List<ExportSite>? Sites { get; init; }
    public List<ExportCriterion>? Criteria { get; init; }
    public List<ExportSpaceGroup>? SpaceGroups { get; init; }
    public List<ExportTemplate>? Templates { get; init; }
    public List<ExportRequestData>? Requests { get; init; }
}

public record ExportSite
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Address { get; init; }
    public ExportSchedulingSettings? SchedulingSettings { get; init; }
    public List<ExportOffTime>? OffTimes { get; init; }
    public List<ExportSpace> Spaces { get; init; } = new();
}

public record ExportSpace
{
    public required string Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public required bool IsPhysical { get; init; }
    public SpaceGeometry? Geometry { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    public string? GroupKey { get; init; }
    public List<ExportCapability>? Capabilities { get; init; }
}

public record ExportCapability
{
    public required string CriterionKey { get; init; }
    public required System.Text.Json.JsonElement Value { get; init; }
}

public record ExportCriterion
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required CriterionDataType DataType { get; init; }
    public List<string>? EnumValues { get; init; }
    public string? Unit { get; init; }
}

public record ExportSpaceGroup
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Color { get; init; }
    public int DisplayOrder { get; init; }
    public List<ExportCapability>? Capabilities { get; init; }
}

public record ExportTemplate
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string EntityType { get; init; }
    public int? DurationValue { get; init; }
    public string? DurationUnit { get; init; }
    public bool FixedStart { get; init; }
    public bool FixedEnd { get; init; }
    public bool FixedDuration { get; init; }
    public List<ExportTemplateItem> Items { get; init; } = new();
}

public record ExportTemplateItem
{
    public required string CriterionKey { get; init; }
    public required string Value { get; init; }
}

public record ExportSchedulingSettings
{
    public required string TimeZone { get; init; }
    public required bool WorkingHoursEnabled { get; init; }
    public required string WorkingDayStart { get; init; }
    public required string WorkingDayEnd { get; init; }
    public required bool WeekendsEnabled { get; init; }
    public required bool PublicHolidaysEnabled { get; init; }
    public string? PublicHolidayRegion { get; init; }
}

public record ExportOffTime
{
    public required string Title { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required OffTimeType Type { get; init; }
    public required bool AppliesToAllSpaces { get; init; }
    public List<string>? SpaceNames { get; init; }
    public required DateTime StartTs { get; init; }
    public required DateTime EndTs { get; init; }
    public required bool IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public required bool Enabled { get; init; }
}

public record ExportRequestData
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? SpaceName { get; init; }
    public string? SiteCode { get; init; }
    public string? RequestItemId { get; init; }
    public DateTime? StartTs { get; init; }
    public DateTime? EndTs { get; init; }
    public DateTime? EarliestStartTs { get; init; }
    public DateTime? LatestEndTs { get; init; }
    public required int MinimalDurationValue { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required DurationUnit MinimalDurationUnit { get; init; }
    public int? ActualDurationValue { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DurationUnit? ActualDurationUnit { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required RequestStatus Status { get; init; }
    public required bool SchedulingSettingsApply { get; init; }
    public List<ExportCapability>? Requirements { get; init; }
}
