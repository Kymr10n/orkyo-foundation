namespace Orkyo.Foundation.Seed.Scales;

public sealed class Medium : IScale
{
    public string Slug => "medium";
    public int Sites => 3;
    public int SpacesPerSite => 170;
    // Balanced against the EQUIPMENT, because utilization is a ratio and the machine/tool catalog
    // is fixed. Measured over the seeded year (NarrativeSeed_FillsTheShop_ToARealisticUtilization):
    // 48 people → 83 % of people busy but only 55 % of stations; 72 → 79 % and 71 %; 84 → 78 % and
    // 80 % at 21k requests. 72 keeps both high with the shop's people as the mild bottleneck, which
    // is the honest shape for a job shop and leaves the slack the conflict demos need. Move this
    // only together with MachineCatalog and the facilities' tool counts.
    public int People => 72;
    public int Departments => 20;
    public int JobTitles => 40;
    public int ResourceGroups => 15;
    public int Criteria => 30;
    public int Templates => 10;
    // Drives the random (non-floorplan) generator only. On the demo's floorplan path volume is a
    // consequence of the utilization targets and the roster above; this just sizes the backlog.
    public int Requests => 4_000;
    public int TimeWindowDays => 270;
}
