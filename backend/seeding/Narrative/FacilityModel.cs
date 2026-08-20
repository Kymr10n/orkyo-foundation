namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// The fixed operational scaffold per facility: its tools (with allocation modes), the kinds of work
/// that happen there (job archetypes — which room, which skills, which tool, how long), and which
/// storage rooms are shared (Fractional, holding several jobs at partial load). Site codes match
/// <c>FloorplanCatalog</c>. The narrative
/// seeder generates the year of jobs from these archetypes against the calendar.
/// </summary>
public enum JobCadence { Campaign, Routine, MonthlyPm, QuarterlyQa }

/// <summary>A tool resource. <paramref name="Role"/> links it to job archetypes; allocation mode is
/// Exclusive (machines) or Fractional (forklifts/cranes, shareable across overlapping jobs).</summary>
public sealed record ToolSpec(string Name, string Role, string AllocationMode, int Count, double? MaxLoadTons = null);

/// <summary>One kind of work. The lead assignee must hold every skill in <paramref name="RequiredSkills"/>.
/// <paramref name="ToolRole"/> (optional) selects a same-facility tool; <paramref name="MachineRole"/>
/// (optional) selects a same-facility machine — a placed resource booked like a room rather than
/// fetched like a tool. A job can name either, and the two are kept separate so converting one kind
/// of work to machines leaves every tool-driven facility untouched.</summary>
public sealed record JobArchetype(
    string Verb, string Noun, string RoomCode, string[] RequiredSkills,
    string? ToolRole, int MinHours, int MaxHours, JobCadence Cadence, int Weight = 1, int TeamSize = 1,
    string? MachineRole = null);

public sealed record Facility(
    string SiteCode,
    IReadOnlyList<ToolSpec> Tools,
    IReadOnlyList<JobArchetype> Archetypes,
    string[] ConcurrentRoomCodes,
    string CampaignName);

public static class FacilityModel
{
    public static readonly IReadOnlyList<Facility> All =
    [
        new Facility("PMF",
            Tools:
            [
                // Mills, lathes and assembly stations are placed machines now, not tools — see
                // MachineCatalog. What is left here is genuinely fetched rather than stood at.
                new ToolSpec("CMM Gauge", "qa", "Exclusive", 1),
                new ToolSpec("Forklift", "forklift", "Fractional", 1, MaxLoadTons: 2.5),
            ],
            Archetypes:
            [
                new JobArchetype("Machine", "precision components", "CNC",  [SkillCatalog.CncOperation], null, 4, 8, JobCadence.Campaign, Weight: 4, TeamSize: 2, MachineRole: MachineCatalog.MillRole),
                new JobArchetype("Turn", "shaft components",        "CNC",  [SkillCatalog.CncOperation], null, 3, 6, JobCadence.Routine, Weight: 2, TeamSize: 1, MachineRole: MachineCatalog.LatheRole),
                new JobArchetype("Mill", "5-axis aerospace parts",  "CNC",  [SkillCatalog.CncOperation], null, 4, 8, JobCadence.Routine, Weight: 2, TeamSize: 2, MachineRole: MachineCatalog.CncRole),
                new JobArchetype("Drill", "fixture holes",          "CNC",  [SkillCatalog.Drilling],     null, 2, 4, JobCadence.Routine, Weight: 2, MachineRole: MachineCatalog.DrillRole),
                new JobArchetype("Assemble", "machined sub-assemblies", "ASSY", [SkillCatalog.Assembly], null, 4, 8, JobCadence.Routine, Weight: 3, TeamSize: 3),
                new JobArchetype("Inspect", "first-article batch",  "QC",   [SkillCatalog.QaInspection], "qa", 2, 4, JobCadence.Routine, Weight: 2, TeamSize: 2),
                new JobArchetype("Receive", "raw stock",            "RAW",  [SkillCatalog.ForkliftLicense], "forklift", 1, 2, JobCadence.Routine, Weight: 2),
                new JobArchetype("Ship", "finished goods",          "FIN",  [SkillCatalog.ForkliftLicense], "forklift", 1, 2, JobCadence.Routine, Weight: 2),
                new JobArchetype("Service", "CNC machine",          "CNC",  [SkillCatalog.Maintenance], null, 2, 4, JobCadence.MonthlyPm, TeamSize: 2, MachineRole: MachineCatalog.MillRole),
                new JobArchetype("Audit", "quality system",         "QC",   [SkillCatalog.QaInspection], null, 4, 6, JobCadence.QuarterlyQa, TeamSize: 2),
            ],
            ConcurrentRoomCodes: ["RAW", "FIN"],
            CampaignName: "Spring Aerospace Bracket Run"),

        new Facility("FWF",
            Tools:
            [
                new ToolSpec("Welding Station", "weld", "Exclusive", 6),
                new ToolSpec("Fabrication Table", "fab", "Exclusive", 4),
                new ToolSpec("Band Saw", "saw", "Exclusive", 2),
                new ToolSpec("Paint Booth Rig", "paint", "Exclusive", 1),
                new ToolSpec("Overhead Crane", "crane", "Fractional", 1, MaxLoadTons: 10),
                new ToolSpec("Forklift", "forklift", "Fractional", 1, MaxLoadTons: 3),
            ],
            Archetypes:
            [
                new JobArchetype("Weld", "structural frames",  "WELD",  [SkillCatalog.WeldingCert], "weld", 4, 8, JobCadence.Campaign, Weight: 4, TeamSize: 2),
                new JobArchetype("Fabricate", "steel components","FAB",  [SkillCatalog.Assembly],    "fab", 4, 8, JobCadence.Routine, Weight: 3, TeamSize: 2),
                new JobArchetype("Paint", "coated assemblies", "PAINT",  [SkillCatalog.Painting],    "paint", 2, 4, JobCadence.Routine, Weight: 2),
                new JobArchetype("Finish", "weld seams",       "GRIND",  [SkillCatalog.Grinding],    null, 2, 4, JobCadence.Routine, Weight: 2),
                new JobArchetype("Drill", "weldment bolt holes","FAB",   [SkillCatalog.Drilling],    null, 2, 4, JobCadence.Routine, Weight: 2, MachineRole: MachineCatalog.DrillRole),
                new JobArchetype("Cut", "steel stock to length","FAB",   [SkillCatalog.MetalCutting],"saw", 2, 4, JobCadence.Routine, Weight: 2),
                new JobArchetype("Lift", "heavy weldments",    "WELD",   [SkillCatalog.CraneOperation], "crane", 1, 2, JobCadence.Routine, Weight: 1),
                new JobArchetype("Receive", "steel stock",     "MAT",    [SkillCatalog.ForkliftLicense], "forklift", 1, 2, JobCadence.Routine, Weight: 2),
                new JobArchetype("Service", "welding equipment","WELD",  [SkillCatalog.Maintenance], "weld", 2, 4, JobCadence.MonthlyPm, TeamSize: 2),
                new JobArchetype("Audit", "weld quality",      "QC",     [SkillCatalog.QaInspection], null, 4, 6, JobCadence.QuarterlyQa, TeamSize: 2),
            ],
            ConcurrentRoomCodes: ["MAT"],
            CampaignName: "Q3 Structural Frames Contract"),

        new Facility("PPF",
            Tools:
            [
                // Assembly and test stations are placed machines now — see MachineCatalog.
                new ToolSpec("Packaging Line", "line", "Exclusive", 2),
                new ToolSpec("Forklift", "forklift", "Fractional", 2, MaxLoadTons: 2.5),
                new ToolSpec("Pallet Jack", "pallet", "Fractional", 2),
            ],
            Archetypes:
            [
                new JobArchetype("Assemble", "product units",  "PROD",  [SkillCatalog.Assembly],      null, 6, 8, JobCadence.Campaign, Weight: 4, TeamSize: 3, MachineRole: MachineCatalog.AssemblyRole),
                new JobArchetype("Test", "finished units",     "QC",    [SkillCatalog.QaInspection],  null, 2, 4, JobCadence.Routine, Weight: 3, TeamSize: 2, MachineRole: MachineCatalog.TestRole),
                new JobArchetype("Pack", "customer orders",    "PKG",   [SkillCatalog.Packaging],     "line", 4, 6, JobCadence.Routine, Weight: 3, TeamSize: 2),
                new JobArchetype("Putaway", "palletised goods","WHSE",  [SkillCatalog.ForkliftLicense], "forklift", 1, 2, JobCadence.Routine, Weight: 2),
                new JobArchetype("Service", "assembly stations","MAINT",[SkillCatalog.Maintenance],   null, 2, 4, JobCadence.MonthlyPm, TeamSize: 2, MachineRole: MachineCatalog.AssemblyRole),
                new JobArchetype("Audit", "assembly compliance","QC",   [SkillCatalog.QaInspection],  null, 4, 6, JobCadence.QuarterlyQa, TeamSize: 2),
            ],
            ConcurrentRoomCodes: ["WHSE"],
            CampaignName: "Holiday Build & Test Surge"),
    ];

    /// <summary>Every person skill any archetype in the facility needs — used to guarantee the
    /// facility's people cohort covers its work.</summary>
    public static IReadOnlyList<string> RequiredPersonSkills(Facility f) =>
        f.Archetypes.SelectMany(a => a.RequiredSkills).Distinct().ToList();

    /// <summary>Every machine role the facility's work needs, so a scaffold test can prove the
    /// machine catalog actually covers what the archetypes ask for.</summary>
    public static IReadOnlyList<string> RequiredMachineRoles(Facility f) =>
        f.Archetypes.Where(a => a.MachineRole is not null).Select(a => a.MachineRole!).Distinct().ToList();
}
