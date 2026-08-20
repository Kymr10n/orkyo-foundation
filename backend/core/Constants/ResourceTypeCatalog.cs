using Api.Models;

namespace Api.Constants;

/// <summary>Which page of the catalog an entry belongs to: fixed-location or mobile.</summary>
public enum CatalogCategory
{
    Stationary,
    Mobile,
}

/// <summary>
/// A custom field a catalog type ships with. Scalar data types only — a shipped field cannot
/// bind a list instance, because instances are tenant data that does not exist until someone
/// creates it. Units live in the label ("Max spindle speed (rpm)"): a custom field describes a
/// value on a form and never drives matching, so nothing needs a real unit.
/// </summary>
public sealed record CatalogFieldSpec(
    string Key,
    string Label,
    string DataType,
    int SortOrder,
    string? Description = null);

/// <summary>One pre-configured resource type the tenant can activate from Configuration.</summary>
public sealed record CatalogTypeSpec(
    string Key,
    string DisplayName,
    string DisplayNamePlural,
    string Description,
    string Icon,
    CatalogCategory Category,
    bool HasGeometry,
    bool HasDirectoryProfile,
    bool SingleGroupMembership,
    IReadOnlyList<CatalogFieldSpec> Fields);

/// <summary>
/// The product-shipped resource type catalog. No type is built in any more: a fresh tenant has
/// an empty resource_types table, and every entry here is a switch under Configuration → Type
/// catalog. Activation instantiates an ordinary, fully editable tenant type — the entry's key
/// is the link, so a tenant's existing `person`/`tool` rows (from the era of built-ins) appear
/// as already activated.
///
/// All fields are optional: a required shipped field would block every existing create flow
/// the moment the type is activated. Stationary types are placeable (has_geometry) and sit in
/// one area at a time (single_group_membership), like the former space type.
/// </summary>
public static class ResourceTypeCatalog
{
    public static readonly IReadOnlyList<CatalogTypeSpec> Types =
    [
        // ── Stationary ────────────────────────────────────────────────────────
        new(
            Key: "drill", DisplayName: "Drill", DisplayNamePlural: "Drills",
            Description: "Pillar and column drilling machines.",
            Icon: "Drill", Category: CatalogCategory.Stationary,
            HasGeometry: true, HasDirectoryProfile: false, SingleGroupMembership: true,
            Fields:
            [
                new("spindle_speed_max", "Max spindle speed (rpm)", CustomFieldDataTypes.Number, 10),
                new("drilling_capacity_steel", "Drilling capacity in steel (mm)", CustomFieldDataTypes.Number, 20),
                new("spindle_taper", "Spindle taper", CustomFieldDataTypes.Text, 30),
                new("coolant_system", "Coolant system", CustomFieldDataTypes.Boolean, 40),
                new("last_service", "Last service", CustomFieldDataTypes.Date, 50),
            ]),
        new(
            Key: "mill", DisplayName: "Mill", DisplayNamePlural: "Mills",
            Description: "Milling machines and machining centres.",
            Icon: "Cog", Category: CatalogCategory.Stationary,
            HasGeometry: true, HasDirectoryProfile: false, SingleGroupMembership: true,
            Fields:
            [
                new("axis_count", "Number of axes", CustomFieldDataTypes.Number, 10),
                new("spindle_speed_max", "Max spindle speed (rpm)", CustomFieldDataTypes.Number, 20),
                new("table_size", "Table size (mm × mm)", CustomFieldDataTypes.Text, 30),
                new("tool_changer_positions", "Tool changer positions", CustomFieldDataTypes.Number, 40),
                new("max_workpiece_weight", "Max workpiece weight (kg)", CustomFieldDataTypes.Number, 50),
            ]),
        new(
            Key: "lathe", DisplayName: "Lathe", DisplayNamePlural: "Lathes",
            Description: "Turning machines, manual and powered.",
            Icon: "Cylinder", Category: CatalogCategory.Stationary,
            HasGeometry: true, HasDirectoryProfile: false, SingleGroupMembership: true,
            Fields:
            [
                new("swing_over_bed", "Swing over bed (mm)", CustomFieldDataTypes.Number, 10),
                new("distance_between_centers", "Distance between centres (mm)", CustomFieldDataTypes.Number, 20),
                new("spindle_bore", "Spindle bore (mm)", CustomFieldDataTypes.Number, 30),
                new("spindle_speed_max", "Max spindle speed (rpm)", CustomFieldDataTypes.Number, 40),
                new("live_tooling", "Live tooling", CustomFieldDataTypes.Boolean, 50),
            ]),
        new(
            Key: "cnc", DisplayName: "CNC Machine", DisplayNamePlural: "CNC Machines",
            Description: "Numerically controlled machining centres.",
            Icon: "Cpu", Category: CatalogCategory.Stationary,
            HasGeometry: true, HasDirectoryProfile: false, SingleGroupMembership: true,
            Fields:
            [
                new("controller", "Controller", CustomFieldDataTypes.Text, 10),
                new("axis_count", "Number of axes", CustomFieldDataTypes.Number, 20),
                new("program_storage_mb", "Program storage (MB)", CustomFieldDataTypes.Number, 30),
                new("maintenance_contract_until", "Maintenance contract until", CustomFieldDataTypes.Date, 40),
                new("documentation_url", "Documentation (URL)", CustomFieldDataTypes.Url, 50),
            ]),
        new(
            Key: "assembly_station", DisplayName: "Assembly Station", DisplayNamePlural: "Assembly Stations",
            Description: "Benches where sub-assemblies are built up.",
            Icon: "Hammer", Category: CatalogCategory.Stationary,
            HasGeometry: true, HasDirectoryProfile: false, SingleGroupMembership: true,
            Fields:
            [
                new("bench_length_m", "Bench length (m)", CustomFieldDataTypes.Number, 10),
                new("max_bench_load_kg", "Max bench load (kg)", CustomFieldDataTypes.Number, 20),
                new("esd_protected", "ESD protected", CustomFieldDataTypes.Boolean, 30),
                new("compressed_air", "Compressed-air supply", CustomFieldDataTypes.Boolean, 40),
            ]),
        new(
            Key: "test_station", DisplayName: "Test Station", DisplayNamePlural: "Test Stations",
            Description: "Stations for inspection, measurement and end-of-line testing.",
            Icon: "Microscope", Category: CatalogCategory.Stationary,
            HasGeometry: true, HasDirectoryProfile: false, SingleGroupMembership: true,
            Fields:
            [
                new("test_types", "Test types", CustomFieldDataTypes.Text, 10),
                new("calibrated_until", "Calibrated until", CustomFieldDataTypes.Date, 20),
                new("calibration_certificate_url", "Calibration certificate (URL)", CustomFieldDataTypes.Url, 30),
                new("climate_controlled", "Climate controlled", CustomFieldDataTypes.Boolean, 40),
            ]),
        new(
            Key: "workstation", DisplayName: "Workstation", DisplayNamePlural: "Workstations",
            Description: "Desks and manual work places.",
            Icon: "Monitor", Category: CatalogCategory.Stationary,
            HasGeometry: true, HasDirectoryProfile: false, SingleGroupMembership: true,
            Fields:
            [
                new("monitor_count", "Number of monitors", CustomFieldDataTypes.Number, 10),
                new("docking_standard", "Docking standard", CustomFieldDataTypes.Text, 20),
                new("height_adjustable", "Height-adjustable desk", CustomFieldDataTypes.Boolean, 30),
                new("esd_protected", "ESD protected", CustomFieldDataTypes.Boolean, 40),
            ]),

        // ── Mobile ────────────────────────────────────────────────────────────
        new(
            Key: "person", DisplayName: "Person", DisplayNamePlural: "People",
            Description: "Operators, fitters and everyone else who does the work.",
            Icon: "Users", Category: CatalogCategory.Mobile,
            HasGeometry: false, HasDirectoryProfile: true, SingleGroupMembership: false,
            // The same fields the demo seed defines (TenantConfigFactory), so a seeded tenant
            // and a catalog activation agree. department/job_title are NOT here: they bind
            // organization-list instances, which the activation path resolves at runtime.
            Fields:
            [
                new("badge_number", "Badge number", CustomFieldDataTypes.Text, 10),
                new("certified_until", "Certified until", CustomFieldDataTypes.Date, 20),
                new("night_shift", "Night shift", CustomFieldDataTypes.Boolean, 30),
            ]),
        new(
            Key: "tool", DisplayName: "Tool", DisplayNamePlural: "Tools",
            Description: "Mobile equipment: hand tools, measurement devices, lifting gear.",
            Icon: "Wrench", Category: CatalogCategory.Mobile,
            HasGeometry: false, HasDirectoryProfile: false, SingleGroupMembership: false,
            Fields:
            [
                new("serial_number", "Serial number", CustomFieldDataTypes.Text, 10),
                new("purchased_on", "Purchased", CustomFieldDataTypes.Date, 20),
                new("last_calibration", "Last calibration", CustomFieldDataTypes.Date, 30),
                new("storage_location", "Storage location", CustomFieldDataTypes.Text, 40),
            ]),
        new(
            Key: "forklift", DisplayName: "Forklift", DisplayNamePlural: "Forklifts",
            Description: "Forklifts and other industrial trucks.",
            Icon: "Forklift", Category: CatalogCategory.Mobile,
            HasGeometry: false, HasDirectoryProfile: false, SingleGroupMembership: false,
            Fields:
            [
                new("load_capacity_t", "Load capacity (t)", CustomFieldDataTypes.Number, 10),
                new("max_lift_height_m", "Max lift height (m)", CustomFieldDataTypes.Number, 20),
                new("power_type", "Power type", CustomFieldDataTypes.Text, 30),
                new("next_inspection", "Next inspection", CustomFieldDataTypes.Date, 40),
                new("indoor_only", "Indoor use only", CustomFieldDataTypes.Boolean, 50),
            ]),
    ];

    public static CatalogTypeSpec? Find(string key) =>
        Types.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
}
