namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// The machines on the shop floor: tenant-defined resource types that declare geometry, so they are
/// placed on the floorplan and scheduled from the stations grid rather than picked from a tool list.
///
/// These were tools once. A tool is a thing you fetch; a mill is a place work happens, which is why
/// it belongs on the plan. Nothing here is a system type — this is exactly what a tenant can build
/// for itself, which is the point of seeding it.
///
/// Geometry is authored in the floorplan image's pixel space (1536×1024) and must sit inside the
/// room it belongs to, so it is a static catalog rather than anything generated.
/// </summary>
public sealed record MachineTypeSpec(
    string Key,
    string DisplayName,
    string DisplayNamePlural,
    string Description,
    /// <summary>lucide-react icon name; an unknown name degrades to a default rather than failing.</summary>
    string Icon,
    /// <summary>
    /// The type's custom fields, mirroring <c>ResourceTypeCatalog</c> entry for entry.
    /// </summary>
    /// <remarks>
    /// Duplicated rather than referenced: this project deliberately does not depend on the API
    /// core (see <c>PeopleFactories</c> and <c>ToolFactory</c> for the same note), so the copy is
    /// kept honest by <c>ResourceTypeCatalogParityTests</c> instead of by the compiler. Without
    /// that test the two drifted — the seed wrote <c>spindle_max_rpm</c> where the catalog wrote
    /// <c>spindle_speed_max</c>, so a seeded mill and an activated mill disagreed on a field key.
    /// </remarks>
    IReadOnlyList<MachineFieldSpec> Fields);

/// <summary>One custom field of a machine type. Data types are the API's field type keys.</summary>
public sealed record MachineFieldSpec(string Key, string Label, string DataType, int SortOrder);

/// <summary>A machine's footprint on the plan. Rectangles are two opposite corners; a circle is a
/// centre and a radius, stored as centre plus one rim point.</summary>
public abstract record MachineGeometry
{
    public sealed record Rect(decimal X, decimal Y, decimal W, decimal H) : MachineGeometry;
    public sealed record Circle(decimal CentreX, decimal CentreY, decimal Radius) : MachineGeometry;
}

/// <summary>
/// One machine. <paramref name="Role"/> links it to job archetypes the way a tool's role does.
/// </summary>
public sealed record MachineSpec(
    string TypeKey,
    string Role,
    string Code,
    string Name,
    string SiteCode,
    /// <summary>The room it stands in, or null when the machine is owned but not placed yet.</summary>
    string? RoomCode,
    /// <summary>
    /// Its footprint, or null when it has never been drawn on the plan.
    /// </summary>
    /// <remarks>
    /// A shop registers equipment long before anyone draws it — imported from a spreadsheet, or
    /// added from its own list. Seeding a few of those gives the floorplan's place-an-existing-
    /// resource flow something to act on in a fresh demo. Room and geometry are null together.
    /// </remarks>
    MachineGeometry? Geometry,
    /// <summary>The cell this machine works in. A cell is a real shop-floor grouping — machines
    /// that together produce a family of parts — and groups are typed, so a cell never mixes
    /// mills with drills.</summary>
    string GroupName);

public static class MachineCatalog
{
    public const string MillRole = "mill";
    public const string DrillRole = "drill";
    public const string LatheRole = "lathe";
    public const string CncRole = "cnc";
    public const string AssemblyRole = "assembly";
    public const string TestRole = "test";

    public static readonly IReadOnlyList<MachineTypeSpec> Types =
    [
        new("mill", "Mill", "Mills",
            "Milling machines and machining centres.",
            "Cog",
            [
                new("axis_count", "Number of axes", "number", 10),
                new("spindle_speed_max", "Max spindle speed (rpm)", "number", 20),
                new("table_size", "Table size (mm × mm)", "text", 30),
                new("tool_changer_positions", "Tool changer positions", "number", 40),
                new("max_workpiece_weight", "Max workpiece weight (kg)", "number", 50),
            ]),
        new("drill", "Drill", "Drills",
            "Pillar and column drilling machines.",
            "Drill",
            [
                new("spindle_speed_max", "Max spindle speed (rpm)", "number", 10),
                new("drilling_capacity_steel", "Drilling capacity in steel (mm)", "number", 20),
                new("spindle_taper", "Spindle taper", "text", 30),
                new("coolant_system", "Coolant system", "boolean", 40),
                new("last_service", "Last service", "date", 50),
            ]),
        new("lathe", "Lathe", "Lathes",
            "Turning machines, manual and powered.",
            "Cylinder",
            [
                new("swing_over_bed", "Swing over bed (mm)", "number", 10),
                new("distance_between_centers", "Distance between centres (mm)", "number", 20),
                new("spindle_bore", "Spindle bore (mm)", "number", 30),
                new("spindle_speed_max", "Max spindle speed (rpm)", "number", 40),
                new("live_tooling", "Live tooling", "boolean", 50),
            ]),
        new("cnc", "CNC Machine", "CNC Machines",
            "Numerically controlled machining centres.",
            "Cpu",
            [
                new("controller", "Controller", "text", 10),
                new("axis_count", "Number of axes", "number", 20),
                new("program_storage_mb", "Program storage (MB)", "number", 30),
                new("maintenance_contract_until", "Maintenance contract until", "date", 40),
                new("documentation_url", "Documentation (URL)", "url", 50),
            ]),
        new("assembly_station", "Assembly Station", "Assembly Stations",
            "Benches where sub-assemblies are built up.",
            "Hammer",
            [
                new("bench_length_m", "Bench length (m)", "number", 10),
                new("max_bench_load_kg", "Max bench load (kg)", "number", 20),
                new("esd_protected", "ESD protected", "boolean", 30),
                new("compressed_air", "Compressed-air supply", "boolean", 40),
            ]),
        new("test_station", "Test Station", "Test Stations",
            "Stations for inspection, measurement and end-of-line testing.",
            "Microscope",
            [
                new("test_types", "Test types", "text", 10),
                new("calibrated_until", "Calibrated until", "date", 20),
                new("calibration_certificate_url", "Calibration certificate (URL)", "url", 30),
                new("climate_controlled", "Climate controlled", "boolean", 40),
            ]),
    ];

    /// <summary>
    /// Machines by facility. Coordinates sit inside the room rectangles authored in
    /// <see cref="Floorplans.FloorplanCatalog"/> — PMF CNC is (430,150) 450×290, FWF FAB is
    /// (430,150) 420×340, PPF PROD is (430,110) 1000×180 and PPF QC is (800,340) 160×160. A pure
    /// test pins that every placed machine stays inside its room.
    /// </summary>
    /// <remarks>
    /// The three sites specialise. PMF machines parts, FWF fabricates and welds them, PPF builds
    /// and tests the finished product — so each site's stations, jobs and skills tell one story
    /// rather than three copies of the same one.
    /// <para>
    /// Every site also owns two or three machines with no shape. They are the subjects for the
    /// floorplan's place-an-existing-resource flow, which has nothing to offer on a plan where
    /// everything is already drawn.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<MachineSpec> All =
    [
        // ── PMF: precision machining ──────────────────────────────────────────
        // Four mills in two cells, two drill presses below them, a turning cell and one 5-axis.
        Mill("PMF", "CNC", 1, 450, 175, "Machining Cell A"),
        Mill("PMF", "CNC", 2, 545, 175, "Machining Cell A"),
        Mill("PMF", "CNC", 3, 640, 175, "Machining Cell B"),
        Mill("PMF", "CNC", 4, 735, 175, "Machining Cell B"),
        Drill("PMF", "CNC", 1, 470, 395, "Drilling Bench"),
        Drill("PMF", "CNC", 2, 545, 395, "Drilling Bench"),
        Lathe("PMF", "CNC", 1, 640, 380, "Turning Cell"),
        Lathe("PMF", "CNC", 2, 720, 380, "Turning Cell"),
        Cnc("PMF", "CNC", 1, 795, 375, "5-Axis Cell"),
        Unplaced("mill", MillRole, "PMF", "VMC", 5, "PMF Mill VMC-5", "Machining Cell B"),
        Unplaced("lathe", LatheRole, "PMF", "LTH", 3, "PMF Lathe LTH-3", "Turning Cell"),
        Unplaced("drill", DrillRole, "PMF", "DRL", 3, "PMF Drill Press 3", "Drilling Bench"),

        // ── FWF: fabrication and welding ──────────────────────────────────────
        // Drills for weldment bolt holes; the welding itself is bench and tool work.
        Drill("FWF", "FAB", 1, 470, 450, "Fabrication Drilling"),
        Drill("FWF", "FAB", 2, 545, 450, "Fabrication Drilling"),
        Unplaced("drill", DrillRole, "FWF", "DRL", 3, "FWF Drill Press 3", "Fabrication Drilling"),
        Unplaced("drill", DrillRole, "FWF", "DRL", 4, "FWF Drill Press 4", "Fabrication Drilling"),

        // ── PPF: assembly and test ────────────────────────────────────────────
        // Two assembly lines down the production hall, end-of-line test in the QC room.
        Assembly("PPF", "PROD", 1, 455, 140, "Assembly Line A"),
        Assembly("PPF", "PROD", 2, 605, 140, "Assembly Line A"),
        Assembly("PPF", "PROD", 3, 755, 140, "Assembly Line B"),
        Assembly("PPF", "PROD", 4, 905, 140, "Assembly Line B"),
        Test("PPF", "QC", 1, 815, 365, "End-of-Line Test"),
        Test("PPF", "QC", 2, 815, 425, "End-of-Line Test"),
        Unplaced("assembly_station", AssemblyRole, "PPF", "ASM", 5, "PPF Assembly Bench 5", "Assembly Line B"),
        Unplaced("assembly_station", AssemblyRole, "PPF", "ASM", 6, "PPF Assembly Bench 6", "Assembly Line B"),
        Unplaced("test_station", TestRole, "PPF", "TST", 3, "PPF Test Rig 3", "End-of-Line Test"),
    ];

    private const decimal MillWidth = 70, MillHeight = 50;
    private const decimal LatheWidth = 70, LatheHeight = 45;
    private const decimal CncWidth = 80, CncHeight = 55;
    private const decimal AssemblyWidth = 110, AssemblyHeight = 65;
    private const decimal TestWidth = 120, TestHeight = 50;
    private const decimal DrillRadius = 22;

    private static MachineSpec Mill(string site, string room, int n, decimal x, decimal y, string group) =>
        new("mill", MillRole, $"{site}-VMC-{n}", $"{site} Mill VMC-{n}", site, room,
            new MachineGeometry.Rect(x, y, MillWidth, MillHeight), group);

    private static MachineSpec Drill(string site, string room, int n, decimal cx, decimal cy, string group) =>
        new("drill", DrillRole, $"{site}-DRL-{n}", $"{site} Drill Press {n}", site, room,
            new MachineGeometry.Circle(cx, cy, DrillRadius), group);

    private static MachineSpec Lathe(string site, string room, int n, decimal x, decimal y, string group) =>
        new("lathe", LatheRole, $"{site}-LTH-{n}", $"{site} Lathe LTH-{n}", site, room,
            new MachineGeometry.Rect(x, y, LatheWidth, LatheHeight), group);

    private static MachineSpec Cnc(string site, string room, int n, decimal x, decimal y, string group) =>
        new("cnc", CncRole, $"{site}-CNC-{n}", $"{site} 5-Axis Centre {n}", site, room,
            new MachineGeometry.Rect(x, y, CncWidth, CncHeight), group);

    private static MachineSpec Assembly(string site, string room, int n, decimal x, decimal y, string group) =>
        new("assembly_station", AssemblyRole, $"{site}-ASM-{n}", $"{site} Assembly Bench {n}", site, room,
            new MachineGeometry.Rect(x, y, AssemblyWidth, AssemblyHeight), group);

    private static MachineSpec Test(string site, string room, int n, decimal x, decimal y, string group) =>
        new("test_station", TestRole, $"{site}-TST-{n}", $"{site} Test Rig {n}", site, room,
            new MachineGeometry.Rect(x, y, TestWidth, TestHeight), group);

    /// <summary>Owned, but never drawn: no room and no shape.</summary>
    private static MachineSpec Unplaced(
        string typeKey, string role, string site, string codePrefix, int n, string name, string group) =>
        new(typeKey, role, $"{site}-{codePrefix}-{n}", name, site, null, null, group);

    public static IEnumerable<MachineSpec> ForSite(string siteCode) =>
        All.Where(m => m.SiteCode == siteCode);
}
