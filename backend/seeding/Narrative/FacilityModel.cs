namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// The fixed operational scaffold per facility: its tools (with allocation modes), the kinds of work
/// that happen there (job archetypes — which room, which skills, which tool, how long), and which
/// storage room runs at ConcurrentCapacity. Site codes match <c>FloorplanCatalog</c>. The narrative
/// seeder generates the year of jobs from these archetypes against the calendar.
/// </summary>
public enum JobCadence { Campaign, Routine, MonthlyPm, QuarterlyQa }

/// <summary>A tool resource. <paramref name="Role"/> links it to job archetypes; allocation mode is
/// Exclusive (machines) or Fractional (forklifts/cranes, shareable across overlapping jobs).</summary>
public sealed record ToolSpec(string Name, string Role, string AllocationMode, int Count, double? MaxLoadTons = null);

/// <summary>One kind of work. The lead assignee must hold every skill in <paramref name="RequiredSkills"/>.
/// <paramref name="ToolRole"/> (optional) selects a same-facility tool.</summary>
public sealed record JobArchetype(
    string Verb, string Noun, string RoomCode, string[] RequiredSkills,
    string? ToolRole, int MinHours, int MaxHours, JobCadence Cadence, int Weight = 1, int TeamSize = 1);

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
                new ToolSpec("CNC Mill", "cnc", "Exclusive", 4),
                new ToolSpec("CNC Lathe", "cnc", "Exclusive", 2),
                new ToolSpec("Assembly Station", "assembly", "Exclusive", 2),
                new ToolSpec("CMM Gauge", "qa", "Exclusive", 1),
                new ToolSpec("Forklift", "forklift", "Fractional", 1, MaxLoadTons: 2.5),
            ],
            Archetypes:
            [
                new JobArchetype("Machine", "precision components", "CNC",  [SkillCatalog.CncOperation], "cnc", 4, 8, JobCadence.Campaign, Weight: 4, TeamSize: 2),
                new JobArchetype("Assemble", "sub-assemblies",      "ASSY", [SkillCatalog.Assembly],     "assembly", 4, 8, JobCadence.Routine, Weight: 3, TeamSize: 3),
                new JobArchetype("Inspect", "first-article batch",  "QC",   [SkillCatalog.QaInspection], "qa", 2, 4, JobCadence.Routine, Weight: 2, TeamSize: 2),
                new JobArchetype("Receive", "raw stock",            "RAW",  [SkillCatalog.ForkliftLicense], "forklift", 1, 2, JobCadence.Routine, Weight: 2),
                new JobArchetype("Ship", "finished goods",          "FIN",  [SkillCatalog.ForkliftLicense], "forklift", 1, 2, JobCadence.Routine, Weight: 2),
                new JobArchetype("Service", "CNC machine",          "CNC",  [SkillCatalog.Maintenance], "cnc", 2, 4, JobCadence.MonthlyPm, TeamSize: 2),
                new JobArchetype("Audit", "quality system",         "QC",   [SkillCatalog.QaInspection], null, 4, 6, JobCadence.QuarterlyQa, TeamSize: 2),
            ],
            ConcurrentRoomCodes: ["RAW", "FIN"],
            CampaignName: "Spring Aerospace Bracket Run"),

        new Facility("FWF",
            Tools:
            [
                new ToolSpec("Welding Station", "weld", "Exclusive", 6),
                new ToolSpec("Fabrication Table", "fab", "Exclusive", 4),
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
                new ToolSpec("Packaging Line", "line", "Exclusive", 2),
                new ToolSpec("Line Station", "station", "Exclusive", 4),
                new ToolSpec("Forklift", "forklift", "Fractional", 2, MaxLoadTons: 2.5),
                new ToolSpec("Pallet Jack", "pallet", "Fractional", 2),
            ],
            Archetypes:
            [
                new JobArchetype("Run", "production line",     "PROD",  [SkillCatalog.LineOperation], "line", 6, 8, JobCadence.Campaign, Weight: 4, TeamSize: 4),
                new JobArchetype("Pack", "customer orders",    "PKG",   [SkillCatalog.Packaging],     "station", 4, 6, JobCadence.Routine, Weight: 3, TeamSize: 3),
                new JobArchetype("Inspect", "outbound quality","QC",    [SkillCatalog.QaInspection],  null, 2, 4, JobCadence.Routine, Weight: 2, TeamSize: 2),
                new JobArchetype("Putaway", "palletised goods","WHSE",  [SkillCatalog.ForkliftLicense], "forklift", 1, 2, JobCadence.Routine, Weight: 2),
                new JobArchetype("Service", "packaging line",  "MAINT", [SkillCatalog.Maintenance],   "line", 2, 4, JobCadence.MonthlyPm, TeamSize: 2),
                new JobArchetype("Audit", "packaging compliance","QC",  [SkillCatalog.QaInspection],  null, 4, 6, JobCadence.QuarterlyQa, TeamSize: 2),
            ],
            ConcurrentRoomCodes: ["WHSE"],
            CampaignName: "Holiday Packaging Surge"),
    ];

    /// <summary>Every person skill any archetype in the facility needs — used to guarantee the
    /// facility's people cohort covers its work.</summary>
    public static IReadOnlyList<string> RequiredPersonSkills(Facility f) =>
        f.Archetypes.SelectMany(a => a.RequiredSkills).Distinct().ToList();
}
