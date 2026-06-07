namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// The skills/specs used across the narrative demo as both criteria, resource capabilities, and
/// request requirements. Person skills drive request→resource matching (a job requires a skill; the
/// assigned lead person has it). Space/tool specs are seeded for realism/display. Values are authored
/// to satisfy <c>CapabilityMatcher</c>: Boolean caps + reqs are both JSON <c>true</c>; Enum requirement
/// allowed_values is the full set so any holder matches; Number specs use a "&gt;=" requirement.
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

    // ── Space / tool specs (display-only) ─────────────────────────────────────
    public const string CleanRoom = "clean_room";
    public const string Ventilated = "ventilated";
    public const string MaxLoadTons = "max_load_tons";

    public static readonly string[] WeldingCertValues = ["MIG", "TIG", "Stick"];

    public static readonly IReadOnlyList<Skill> All =
    [
        new(CncOperation,     "CNC Operation",         "Boolean", null, null, SkillKind.Person),
        new(Assembly,         "Assembly",              "Boolean", null, null, SkillKind.Person),
        new(LineOperation,    "Line Operation",        "Boolean", null, null, SkillKind.Person),
        new(Packaging,        "Packaging",             "Boolean", null, null, SkillKind.Person),
        new(QaInspection,     "QA Inspection",         "Boolean", null, null, SkillKind.Person),
        new(ForkliftLicense,  "Forklift License",      "Boolean", null, null, SkillKind.Person),
        new(CraneOperation,   "Crane Operation",       "Boolean", null, null, SkillKind.Person),
        new(Painting,         "Painting",              "Boolean", null, null, SkillKind.Person),
        new(Grinding,         "Grinding & Finishing",  "Boolean", null, null, SkillKind.Person),
        new(Maintenance,      "Maintenance",           "Boolean", null, null, SkillKind.Person),
        new(WeldingCert,      "Welding Certification", "Enum",    WeldingCertValues, null, SkillKind.Person),

        new(CleanRoom,        "Clean Room",            "Boolean", null, null, SkillKind.SpaceSpec),
        new(Ventilated,       "Ventilated",            "Boolean", null, null, SkillKind.SpaceSpec),
        new(MaxLoadTons,      "Max Load",              "Number",  null, "t",  SkillKind.ToolSpec),
    ];

    public static Skill ByKey(string key) =>
        All.FirstOrDefault(s => s.Key == key)
        ?? throw new KeyNotFoundException($"Unknown skill '{key}'.");
}
