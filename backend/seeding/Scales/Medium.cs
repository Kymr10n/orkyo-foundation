namespace Orkyo.Foundation.Seed.Scales;

public sealed class Medium : IScale
{
    public string Slug => "medium";
    public int Sites => 3;
    public int SpacesPerSite => 170;
    public int People => 300;
    public int Departments => 20;
    public int JobTitles => 40;
    public int ResourceGroups => 15;
    public int Criteria => 30;
    public int Templates => 10;
    public int Requests => 2_000;
    public int TimeWindowDays => 270;
}
