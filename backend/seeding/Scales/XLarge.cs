namespace Orkyo.Foundation.Seed.Scales;

public sealed class XLarge : IScale
{
    public string Slug => "xlarge";
    public int Sites => 10;
    public int SpacesPerSite => 500;
    public int People => 1_500;
    public int Departments => 50;
    public int JobTitles => 100;
    public int ResourceGroups => 40;
    public int Criteria => 50;
    public int Templates => 20;
    public int Requests => 15_000;
    public int TimeWindowDays => 1_080;
}
