namespace Orkyo.Foundation.Seed.Profiles;

public sealed class Education : IProfile
{
    public string Slug => "education";
    public string DisplayName => "Education";

    public IReadOnlyList<string> SiteNamePool { get; } = new[]
    {
        "Campus North", "Campus South", "Annex Building", "Science Wing",
        "Arts Wing", "Library Building", "Sports Complex",
    };

    public string SpaceNameTemplate => "Room {0}";

    public IReadOnlyList<string> JobTitlePool { get; } = new[]
    {
        "Lecturer", "Senior Lecturer", "Professor", "TA", "Lab Tech",
        "Registrar", "Counsellor", "Librarian", "Coach", "Administrator",
    };

    public IReadOnlyList<string> DepartmentRootPool { get; } = new[]
    {
        "Sciences", "Humanities", "Arts", "Engineering", "Administration", "Athletics",
    };

    public IReadOnlyList<string> ResourceGroupPool { get; } = new[]
    {
        "Lecture Halls", "Seminar Rooms", "Wet Labs", "Computer Labs",
        "Studios", "Sports Halls", "Music Rooms", "Study Pods",
    };

    public IReadOnlyList<string> PersonGroupPool { get; } = new[]
    {
        "Teaching Staff", "Lab Technicians", "Administrative Staff", "Support Staff",
        "Research Team", "Faculty Leadership", "Student Services", "IT Team",
    };

    public IReadOnlyList<string> RequestNameVerbs { get; } = new[]
    {
        "Teach", "Schedule", "Review", "Grade", "Hold", "Set up", "Run",
        "Lead", "Proctor",
    };

    public IReadOnlyList<string> RequestNameNouns { get; } = new[]
    {
        "Calc 101 lecture", "lab session 3", "midterm exam", "office hours",
        "department meeting", "career fair", "open house", "study group",
    };
}
