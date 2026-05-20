namespace Orkyo.Foundation.Seed.Profiles;

public sealed class Generic : IProfile
{
    public string Slug => "generic";
    public string DisplayName => "Generic";

    public IReadOnlyList<string> SiteNamePool { get; } = new[]
    {
        "Region HQ", "Branch North", "Branch South", "Branch East", "Branch West",
        "Downtown Office", "Coastal Hub", "Riverside Annex", "Hilltop Center", "Plaza Tower",
    };

    public string SpaceNameTemplate => "Room {0}";

    public IReadOnlyList<string> JobTitlePool { get; } = new[]
    {
        "Coordinator", "Specialist", "Team Lead", "Manager", "Senior Analyst", "Analyst",
        "Project Lead", "Associate", "Director", "Consultant",
    };

    public IReadOnlyList<string> DepartmentRootPool { get; } = new[]
    {
        "Operations", "Finance", "People", "Engineering", "Sales", "Support", "Strategy",
    };

    public IReadOnlyList<string> ResourceGroupPool { get; } = new[]
    {
        "Meeting Rooms", "Workstations", "Phone Booths", "Quiet Zones", "Lab Bays",
        "Storage", "Studio Space", "Lounge", "Workshop", "Outdoor",
    };

    public IReadOnlyList<string> RequestNameVerbs { get; } = new[]
    {
        "Plan", "Review", "Draft", "Launch", "Pilot", "Refresh", "Migrate", "Onboard",
        "Audit", "Roll out", "Coordinate", "Schedule",
    };

    public IReadOnlyList<string> RequestNameNouns { get; } = new[]
    {
        "Q3 deliverables", "client kickoff", "quarterly review", "team workshop",
        "vendor handoff", "demo session", "training cohort", "stakeholder sync",
        "operational dry-run", "design review", "release prep", "open day",
    };
}
