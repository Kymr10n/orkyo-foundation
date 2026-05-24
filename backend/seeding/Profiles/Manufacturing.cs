namespace Orkyo.Foundation.Seed.Profiles;

public sealed class Manufacturing : IProfile
{
    public string Slug => "manufacturing";
    public string DisplayName => "Manufacturing";

    public IReadOnlyList<string> SiteNamePool { get; } = new[]
    {
        "Plant 1", "Plant 2", "Line North", "Line South", "Assembly Hall",
        "Distribution Center", "Maintenance Hub", "QA Annex",
    };

    public string SpaceNameTemplate => "Cell {0}";

    public IReadOnlyList<string> JobTitlePool { get; } = new[]
    {
        "Operator", "Senior Operator", "Foreman", "Shift Lead", "QA Tech", "QA Engineer",
        "Maintenance Tech", "Process Engineer", "Production Manager", "Logistics Coordinator",
    };

    public IReadOnlyList<string> DepartmentRootPool { get; } = new[]
    {
        "Production", "Quality", "Maintenance", "Logistics", "Engineering", "Safety",
    };

    public IReadOnlyList<string> ResourceGroupPool { get; } = new[]
    {
        "CNC Cells", "Assembly Bays", "Welding Stations", "Paint Booths", "QA Stations",
        "Packaging Lines", "Calibration Rooms", "Tool Cribs",
    };

    public IReadOnlyList<string> PersonGroupPool { get; } = new[]
    {
        "Production Crew", "Quality Team", "Maintenance Team", "Logistics Team",
        "Engineering Team", "Management Team", "Shift A", "Shift B",
    };

    public IReadOnlyList<string> RequestNameVerbs { get; } = new[]
    {
        "Run", "Inspect", "Calibrate", "Retool", "Repair", "Audit", "Schedule",
        "Validate", "Test", "Ship",
    };

    public IReadOnlyList<string> RequestNameNouns { get; } = new[]
    {
        "batch #312", "tooling changeover", "preventive maintenance",
        "first-article inspection", "weekly QA sweep", "shift handover",
        "supplier audit", "process validation", "production rampup",
    };
}
