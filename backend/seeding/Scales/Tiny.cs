namespace Orkyo.Foundation.Seed.Scales;

public sealed class Tiny : IScale
{
    public string Slug => "tiny";
    public int Sites => 1;
    public int SpacesPerSite => 20;
    public int People => 15;
    public int Departments => 3;
    public int JobTitles => 10;
    public int ResourceGroups => 3;
    public int Criteria => 10;
    public int Templates => 3;
    public int Requests => 100;
    public int TimeWindowDays => 60;
}
