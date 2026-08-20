namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// Who works at each facility, as roles rather than as four unrelated random draws.
/// </summary>
/// <remarks>
/// A person used to get a job title, a department, a team and a skill set from four independent
/// shuffles, which produced a QA Tech in Logistics East on the Production Crew holding a forklift
/// licence. Nothing about that is wrong in the data model and everything about it is wrong in a
/// demo: the reader cannot tell the model from the noise.
/// <para>
/// A persona ties the four together. The roster is applied by index — person <c>j</c> of a cohort
/// takes <c>roster[j % roster.Count]</c> — so it needs no randomness and every pass that has to
/// recompute it (skills here, org fields in <c>PersonaFactory</c>) reaches the same answer.
/// </para>
/// </remarks>
public sealed record Persona(
    string Role,
    /// <summary>Must be a name from the profile's job-title pool, so the seeded list holds it.</summary>
    string JobTitle,
    /// <summary>A department name; resolved to the child row, then the root, then left unset.</summary>
    string Department,
    string[] Skills);

public static class PersonaCatalog
{
    /// <summary>PMF machines parts: operators, a toolroom, inspection, maintenance, logistics.</summary>
    private static readonly IReadOnlyList<Persona> Machining =
    [
        new("CNC Machinist",         "Senior Operator",       "Production Machining",     [SkillCatalog.CncOperation]),
        new("Machinist",             "Operator",              "Production Machining",     [SkillCatalog.CncOperation, SkillCatalog.Drilling]),
        new("Toolroom Operator",     "Operator",              "Production Tooling",       [SkillCatalog.Drilling]),
        new("Sub-Assembly Fitter",   "Operator",              "Production Assembly & Test", [SkillCatalog.Assembly]),
        new("Quality Inspector",     "QA Tech",               "Quality Calibration",      [SkillCatalog.QaInspection]),
        new("Maintenance Technician","Maintenance Tech",      "Maintenance Machining",    [SkillCatalog.Maintenance]),
        new("Materials Handler",     "Logistics Coordinator", "Logistics Inbound",        [SkillCatalog.ForkliftLicense]),
        new("Shift Lead",            "Shift Lead",            "Production Machining",     [SkillCatalog.CncOperation, SkillCatalog.QaInspection]),
    ];

    /// <summary>FWF fabricates and welds: welders, fabricators, finishing, a weld inspector.</summary>
    private static readonly IReadOnlyList<Persona> Fabrication =
    [
        new("Welder",                "Operator",              "Production Fabrication",   [SkillCatalog.WeldingCert]),
        new("Senior Welder",         "Senior Operator",       "Production Fabrication",   [SkillCatalog.WeldingCert, SkillCatalog.Grinding]),
        new("Fabricator",            "Operator",              "Production Fabrication",   [SkillCatalog.Assembly, SkillCatalog.MetalCutting]),
        new("Saw & Drill Operator",  "Operator",              "Production Fabrication",   [SkillCatalog.MetalCutting, SkillCatalog.Drilling]),
        new("Painter",               "Operator",              "Production Fabrication",   [SkillCatalog.Painting]),
        new("Finisher",              "Operator",              "Production Fabrication",   [SkillCatalog.Grinding]),
        new("Crane & Forklift Operator","Logistics Coordinator","Logistics Inbound",      [SkillCatalog.CraneOperation, SkillCatalog.ForkliftLicense]),
        new("Weld QA Inspector",     "QA Engineer",           "Quality Fabrication",      [SkillCatalog.QaInspection]),
        new("Maintenance Technician","Maintenance Tech",      "Maintenance Fabrication",  [SkillCatalog.Maintenance]),
    ];

    /// <summary>PPF builds and tests the finished product: assemblers, testers, packing.</summary>
    private static readonly IReadOnlyList<Persona> AssemblyAndTest =
    [
        new("Assembler",             "Operator",              "Production Assembly & Test", [SkillCatalog.Assembly]),
        new("Senior Assembler",      "Senior Operator",       "Production Assembly & Test", [SkillCatalog.Assembly, SkillCatalog.Packaging]),
        new("Test Technician",       "QA Tech",               "Quality Assembly & Test",  [SkillCatalog.QaInspection]),
        new("QA Engineer",           "QA Engineer",           "Quality Assembly & Test",  [SkillCatalog.QaInspection]),
        new("Packer",                "Operator",              "Production Packaging",     [SkillCatalog.Packaging]),
        new("Warehouse Operator",    "Logistics Coordinator", "Logistics Outbound",       [SkillCatalog.ForkliftLicense]),
        new("Maintenance Technician","Maintenance Tech",      "Maintenance Assembly & Test", [SkillCatalog.Maintenance]),
        new("Line Lead",             "Shift Lead",            "Production Assembly & Test", [SkillCatalog.Assembly, SkillCatalog.QaInspection]),
    ];

    /// <summary>The roster for one facility. An unknown code falls back to machining rather than
    /// throwing: a profile without a persona set still seeds people who can do its work.</summary>
    public static IReadOnlyList<Persona> Roster(string siteCode) => siteCode switch
    {
        "FWF" => Fabrication,
        "PPF" => AssemblyAndTest,
        _ => Machining,
    };

    /// <summary>The persona person <paramref name="index"/> of a cohort holds.</summary>
    public static Persona For(string siteCode, int index)
    {
        var roster = Roster(siteCode);
        return roster[index % roster.Count];
    }
}
