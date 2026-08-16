using System.Text.Json;
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
    /// <summary>
    /// Resources that are not placed on a floorplan — people, tools and every
    /// tenant-defined type. Placeable ones stay nested under their site (see
    /// <see cref="ExportSite.Spaces"/>), which is where they are managed.
    /// </summary>
    public List<ExportResource>? Resources { get; init; }

    /// <summary>
    /// List definitions with their columns, and the shared instances built from them, rows
    /// included. Per-resource list rows are NOT here: they belong to one resource each and would
    /// need the resource identity to round-trip, which is its own piece of work (follow-up
    /// issue). Lookup values already travel inside <see cref="ExportResource.CustomFields"/> as
    /// the row ids they are, so a shared list and the picks referencing it export together.
    /// </summary>
    public List<ExportListDefinition>? ListDefinitions { get; init; }
}

/// <summary>A list definition: the reusable shape, its columns, and its shared instances.</summary>
public record ExportListDefinition
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public required List<ExportListColumn> Columns { get; init; }
    public required List<ExportListInstance> SharedInstances { get; init; }
}

public record ExportListColumn
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string DataType { get; init; }
    /// <summary>Declared options for a `select` column; absent for every other type.</summary>
    public List<string>? Options { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}

public record ExportListInstance
{
    public required string Name { get; init; }
    /// <summary>
    /// Rows carry their id because lookup values reference them by id: dropping it would export
    /// a selection that names nothing.
    /// </summary>
    public required List<ExportListRow> Rows { get; init; }
}

public record ExportListRow
{
    public required Guid Id { get; init; }
    public required Dictionary<string, JsonElement> Values { get; init; }
}

/// <summary>
/// A resource of any non-placeable type. Type-specific fields live in
/// <see cref="Metadata"/>, so a tenant's own type exports without this record
/// ever knowing what its fields are.
/// </summary>
public record ExportResource
{
    public required string ResourceTypeKey { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ExternalReference { get; init; }
    public required string AllocationMode { get; init; }
    public int BaseAvailabilityPercent { get; init; }
    public bool CrossSiteAllowed { get; init; }
    /// <summary>Code of the resource's home site; null when it belongs to no site.</summary>
    public string? HomeSiteCode { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    /// <summary>Values for the resource type's custom fields, keyed by field key.</summary>
    public Dictionary<string, JsonElement>? CustomFields { get; init; }
    public List<ExportCapability>? Capabilities { get; init; }
}

public record ExportSite
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Address { get; init; }
    public ExportSchedulingSettings? SchedulingSettings { get; init; }
    public List<ExportAvailabilityEvent>? AvailabilityEvents { get; init; }
    public List<ExportSpace> Spaces { get; init; } = new();
}

public record ExportSpace
{
    public required string Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public required bool IsPhysical { get; init; }
    public ResourceGeometry? Geometry { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    /// <summary>Values for the space type's custom fields, keyed by field key.</summary>
    public Dictionary<string, JsonElement>? CustomFields { get; init; }
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

public record ExportAvailabilityEvent
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required AvailabilityEventType EventType { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required DefaultEffect DefaultEffect { get; init; }
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
    public string? ResourceName { get; init; }
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
