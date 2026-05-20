namespace Orkyo.Foundation.Seed.Scales;

public sealed class Large : IScale
{
    public string Slug => "large";
    public int Sites => 5;
    public int SpacesPerSite => 300;
    public int People => 600;
    public int Departments => 30;
    public int JobTitles => 60;
    public int ResourceGroups => 25;
    public int Criteria => 40;
    public int Templates => 15;
    public int Requests => 6_000;
    public int TimeWindowDays => 540;
}
