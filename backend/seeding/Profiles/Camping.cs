namespace Orkyo.Foundation.Seed.Profiles;

public sealed class Camping : IProfile
{
    public string Slug => "camping";
    public string DisplayName => "Camping";

    public IReadOnlyList<string> SiteNamePool { get; } = new[]
    {
        "Lake Camp", "Forest Site", "Riverbend Resort", "Coastal Camp",
        "Mountain Retreat", "Meadow Park", "Pine Hollow",
    };

    public string SpaceNameTemplate => "Pitch {0}";

    public IReadOnlyList<string> JobTitlePool { get; } = new[]
    {
        "Warden", "Cleaner", "Lifeguard", "Receptionist", "Maintenance",
        "Activity Lead", "Cook", "Night Watch", "Naturalist Guide",
    };

    public IReadOnlyList<string> DepartmentRootPool { get; } = new[]
    {
        "Reception", "Housekeeping", "Activities", "Maintenance", "Food & Beverage", "Safety",
    };

    public IReadOnlyList<string> ResourceGroupPool { get; } = new[]
    {
        "Tent Pitches", "Cabins", "Caravans", "Beach Pitches", "Pet-friendly",
        "Family Plots", "Quiet Zone", "Group Camp",
    };

    public IReadOnlyList<string> PersonGroupPool { get; } = new[]
    {
        "Reception Staff", "Maintenance Team", "Catering Crew", "Activity Leaders",
        "Security Team", "Housekeeping", "Management",
    };

    public IReadOnlyList<string> RequestNameVerbs { get; } = new[]
    {
        "Host", "Reserve", "Welcome", "Set up", "Prepare", "Inspect", "Clean",
        "Coordinate", "Lead",
    };

    public IReadOnlyList<string> RequestNameNouns { get; } = new[]
    {
        "Smith family stay", "weekend retreat", "school camp", "beach BBQ",
        "evening campfire", "cabin handover", "ranger tour", "stargazing night",
    };
}
