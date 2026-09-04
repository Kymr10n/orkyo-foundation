namespace Orkyo.Foundation.Seed.Scales;

public sealed class Medium : IScale
{
    public string Slug => "medium";
    public int Sites => 3;
    public int SpacesPerSite => 170;
    // Sized to the work, not the other way round: ~4k requests staff one lead and the odd
    // helper each, so a 300-person roster sat below 2 % utilization and read as a ghost shop.
    public int People => 72;
    public int Departments => 20;
    public int JobTitles => 40;
    public int ResourceGroups => 15;
    public int Criteria => 30;
    public int Templates => 10;
    public int Requests => 4_000;
    public int TimeWindowDays => 270;
}
