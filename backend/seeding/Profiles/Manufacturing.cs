namespace Orkyo.Foundation.Seed.Profiles;

public sealed class Manufacturing : IProfile
{
    public string Slug => "manufacturing";

    public IReadOnlyList<string> JobTitlePool { get; } = new[]
    {
        "Operator", "Senior Operator", "Foreman", "Shift Lead", "QA Tech", "QA Engineer",
        "Maintenance Tech", "Process Engineer", "Production Manager", "Logistics Coordinator",
    };

    public IReadOnlyList<string> DepartmentRootPool { get; } = new[]
    {
        "Production", "Quality", "Maintenance", "Logistics", "Engineering", "Safety",
    };

    public IReadOnlyList<string> DepartmentChildPool { get; } = new[]
    {
        "Machining", "Fabrication", "Assembly & Test", "Packaging", "Tooling", "Inbound", "Outbound", "Calibration", "Day Shift", "Night Shift"
    };
}
