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
    string Icon);

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
    string RoomCode,
    MachineGeometry Geometry,
    /// <summary>The cell this machine works in. A cell is a real shop-floor grouping — machines
    /// that together produce a family of parts — and groups are typed, so a cell never mixes
    /// mills with drills.</summary>
    string GroupName);

public static class MachineCatalog
{
    public const string MillRole = "mill";
    public const string DrillRole = "drill";
    public const string AssemblyRole = "assembly";

    public static readonly IReadOnlyList<MachineTypeSpec> Types =
    [
        new("mill", "Mill", "Mills",
            "Vertical machining centre. Occupies a bay on the floorplan and is booked for one job at a time.",
            "Cog"),
        new("drill", "Drill", "Drills",
            "Pillar drill press. Round footprint, kept near the machining bays.",
            "CircleDot"),
        new("assembly_station", "Assembly Station", "Assembly Stations",
            "Bench where sub-assemblies are built up. Booked for one job at a time.",
            "Hammer"),
    ];

    /// <summary>
    /// Machines by facility. Coordinates sit inside the room rectangles authored in
    /// <see cref="Floorplans.FloorplanCatalog"/> — PMF CNC is (430,150) 450×290 and PMF ASSY is
    /// (930,150) 510×290, FWF FAB is (430,150) 420×340. A pure test pins that they stay inside.
    /// </summary>
    public static readonly IReadOnlyList<MachineSpec> All =
    [
        // PMF CNC bay: four mills in two cells, two drill presses below them.
        Mill("PMF", "CNC", 1, 450, 175, "Machining Cell A"),
        Mill("PMF", "CNC", 2, 545, 175, "Machining Cell A"),
        Mill("PMF", "CNC", 3, 640, 175, "Machining Cell B"),
        Mill("PMF", "CNC", 4, 735, 175, "Machining Cell B"),
        Drill("PMF", "CNC", 1, 470, 395, "Drilling Bench"),
        Drill("PMF", "CNC", 2, 545, 395, "Drilling Bench"),

        // PMF assembly bay.
        Assembly("PMF", "ASSY", 1, 955, 180, "Sub-Assembly Line"),
        Assembly("PMF", "ASSY", 2, 1090, 180, "Sub-Assembly Line"),

        // A drill at the fabrication shop: owned, placed, and never scheduled — plenty of real
        // equipment is exactly that, and it proves placement does not depend on planning.
        Drill("FWF", "FAB", 1, 470, 450, "Fabrication Drilling"),
    ];

    private const decimal MillWidth = 70, MillHeight = 50;
    private const decimal AssemblyWidth = 110, AssemblyHeight = 65;
    private const decimal DrillRadius = 22;

    private static MachineSpec Mill(string site, string room, int n, decimal x, decimal y, string group) =>
        new("mill", MillRole, $"{site}-VMC-{n}", $"{site} Mill VMC-{n}", site, room,
            new MachineGeometry.Rect(x, y, MillWidth, MillHeight), group);

    private static MachineSpec Drill(string site, string room, int n, decimal cx, decimal cy, string group) =>
        new("drill", DrillRole, $"{site}-DRL-{n}", $"{site} Drill Press {n}", site, room,
            new MachineGeometry.Circle(cx, cy, DrillRadius), group);

    private static MachineSpec Assembly(string site, string room, int n, decimal x, decimal y, string group) =>
        new("assembly_station", AssemblyRole, $"{site}-ASM-{n}", $"{site} Assembly Bench {n}", site, room,
            new MachineGeometry.Rect(x, y, AssemblyWidth, AssemblyHeight), group);

    public static IEnumerable<MachineSpec> ForSite(string siteCode) =>
        All.Where(m => m.SiteCode == siteCode);
}
