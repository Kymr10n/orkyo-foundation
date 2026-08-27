namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// The skills/specs used across the narrative demo as both criteria, resource capabilities, and
/// request requirements. Person skills drive request→resource matching (a job requires a skill; the
/// assigned lead person has it). Space/tool specs are seeded for realism/display. Values are authored
/// to satisfy <c>CapabilityMatcher</c>: Boolean caps + reqs are both JSON <c>true</c>; Enum requirement
/// allowed_values is the full set so any holder matches; Number specs use a "&gt;=" requirement.
/// </summary>
/// <summary>
/// What a skill describes. Read by the scaffold test that asserts every archetype names a real
/// person skill — not by the seeder, which maps a skill to resource types through
/// CapabilityFactory.TypesFor. Two views of one fact; if they ever disagree, TypesFor is the
/// one the database sees.
/// </summary>
public enum SkillKind { Person, SpaceSpec, ToolSpec }

public sealed record Skill(
    string Key,
    string Name,
    string DataType,            // "Boolean" | "Number" | "Enum"
    string[]? EnumValues,
    string? Unit,
    SkillKind Kind);

public static class SkillCatalog
{
    // ── Person skills (requirement-bearing) ───────────────────────────────────
    public const string CncOperation = "cnc_operation";
    public const string Assembly = "assembly";
    public const string LineOperation = "line_operation";
    public const string Packaging = "packaging";
    public const string QaInspection = "qa_inspection";
    public const string ForkliftLicense = "forklift_license";
    public const string CraneOperation = "crane_operation";
    public const string Painting = "painting";
    public const string Grinding = "grinding";
    public const string Maintenance = "maintenance";
    public const string WeldingCert = "welding_cert";
    public const string Drilling = "drilling";
    public const string MetalCutting = "metal_cutting";
    /// <summary>
    /// Held by nobody, on purpose. No archetype requires it and no persona carries it, so the two
    /// backlog items that ask for it are the ones auto-scheduling reports as having no compatible
    /// resource — the honest half of the solver demo.
    /// </summary>
    public const string WeldInspection = "weld_inspection";

    // ── Space / tool specs (display-only) ─────────────────────────────────────
    public const string CleanRoom = "clean_room";
    public const string Ventilated = "ventilated";
    public const string MaxLoadTons = "max_load_tons";

    public static readonly string[] WeldingCertValues = ["MIG", "TIG", "Stick"];

    public static readonly IReadOnlyList<Skill> All =
    [
        new(CncOperation,     "CNC Operation",         "Boolean", null, null, SkillKind.Person),
        new(Assembly,         "Assembly",              "Boolean", null, null, SkillKind.Person),
        new(Packaging,        "Packaging",             "Boolean", null, null, SkillKind.Person),
        new(QaInspection,     "QA Inspection",         "Boolean", null, null, SkillKind.Person),
        new(ForkliftLicense,  "Forklift License",      "Boolean", null, null, SkillKind.Person),
        new(CraneOperation,   "Crane Operation",       "Boolean", null, null, SkillKind.Person),
        new(Painting,         "Painting",              "Boolean", null, null, SkillKind.Person),
        new(Grinding,         "Grinding & Finishing",  "Boolean", null, null, SkillKind.Person),
        new(Maintenance,      "Maintenance",           "Boolean", null, null, SkillKind.Person),
        new(WeldingCert,      "Welding Certification", "Enum",    WeldingCertValues, null, SkillKind.Person),
        new(Drilling,         "Drilling",              "Boolean", null, null, SkillKind.Person),
        new(MetalCutting,     "Metal Cutting & Sawing","Boolean", null, null, SkillKind.Person),
        new(WeldInspection,   "Certified Weld Inspector","Boolean",null, null, SkillKind.Person),

        new(CleanRoom,        "Clean Room",            "Boolean", null, null, SkillKind.SpaceSpec),
        new(Ventilated,       "Ventilated",            "Boolean", null, null, SkillKind.SpaceSpec),
        new(MaxLoadTons,      "Max Load",              "Number",  null, "t",  SkillKind.ToolSpec),
    ];

    public static Skill ByKey(string key) =>
        All.FirstOrDefault(s => s.Key == key)
        // A miss here is a seeding-profile programming error, not a lookup that can
        // legitimately fail — the keys are compile-time constants in this file.
        ?? throw new InvalidOperationException($"Unknown skill '{key}'.");
}
