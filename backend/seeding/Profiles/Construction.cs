namespace Orkyo.Foundation.Seed.Profiles;

public sealed class Construction : IProfile
{
    public string Slug => "construction";
    public string DisplayName => "Construction";

    public IReadOnlyList<string> SiteNamePool { get; } = new[]
    {
        "Site Alpha", "Site Beta", "Site Gamma", "Yard 3", "Yard 7", "North Project",
        "South Project", "Bridge Crew HQ", "Tunnel Annex",
    };

    public string SpaceNameTemplate => "Zone {0}";

    public IReadOnlyList<string> JobTitlePool { get; } = new[]
    {
        "Foreman", "Carpenter", "Electrician", "Plumber", "Welder", "Surveyor",
        "Site Engineer", "Safety Officer", "Equipment Operator", "Laborer",
    };

    public IReadOnlyList<string> DepartmentRootPool { get; } = new[]
    {
        "Structural", "Electrical", "Mechanical", "Civil", "Safety", "Logistics",
    };

    public IReadOnlyList<string> ResourceGroupPool { get; } = new[]
    {
        "Excavation", "Foundations", "Framing", "MEP", "Finishing", "Crane Zones",
        "Storage Yards", "Site Offices",
    };

    public IReadOnlyList<string> RequestNameVerbs { get; } = new[]
    {
        "Pour", "Erect", "Install", "Inspect", "Survey", "Excavate", "Tear down",
        "Wire up", "Plumb", "Commission",
    };

    public IReadOnlyList<string> RequestNameNouns { get; } = new[]
    {
        "north wing slab", "façade panels", "mezzanine framing", "MEP rough-in",
        "tower crane move", "weekly safety walk", "drainage trench", "scaffold takedown",
    };
}
