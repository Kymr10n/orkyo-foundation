namespace Orkyo.Foundation.Seed.Scales;

public sealed class Small : IScale
{
    public string Slug => "small";
    public int Sites => 2;
    public int SpacesPerSite => 50;
    public int People => 60;
    public int Departments => 8;
    public int JobTitles => 20;
    public int ResourceGroups => 8;
    public int Criteria => 20;
    public int Templates => 6;
    public int Requests => 500;
    public int TimeWindowDays => 120;
}
