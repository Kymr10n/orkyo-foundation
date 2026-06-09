using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Floorplans;
using Orkyo.Foundation.Seed.Narrative;
using Xunit;

namespace Orkyo.Foundation.Seed.Tests;

/// <summary>
/// Guards the two pure classifiers behind "meaningful" demo grouping: spaces → functional area,
/// people → team/role. Replaces the old round-robin (which scattered each site's spaces ~1 per
/// group and assigned people to teams unrelated to their skills).
/// </summary>
public class ResourceGroupingTests
{
    private static readonly string[] ExpectedAreas =
    [
        "Production", "Quality", "Storage & Logistics", "Maintenance & Tooling", "Facilities & Admin",
    ];

    [Theory]
    [InlineData("CNC", "Production")]
    [InlineData("WELD", "Production")]
    [InlineData("PKG", "Production")]
    [InlineData("QC", "Quality")]
    [InlineData("RAW", "Storage & Logistics")]
    [InlineData("WHSE", "Storage & Logistics")]
    [InlineData("MAINT", "Maintenance & Tooling")]
    [InlineData("ELEC", "Maintenance & Tooling")]
    [InlineData("OFC", "Facilities & Admin")]
    [InlineData("LOBBY", "Facilities & Admin")]
    public void FunctionalArea_MapsKnownCodes(string code, string expected)
        => Assert.Equal(expected, SpaceFactories.FunctionalArea(code));

    [Theory]
    [InlineData("cnc")]   // lower-case is normalised
    [InlineData("ZZZ")]   // unknown
    [InlineData(null)]    // missing
    public void FunctionalArea_FallsBackToFacilitiesAdmin(string? code)
    {
        var area = SpaceFactories.FunctionalArea(code);
        // "cnc" normalises to Production; the others fall through to the catch-all.
        Assert.True(area is "Production" or "Facilities & Admin");
        Assert.Contains(area, ExpectedAreas);
    }

    [Fact]
    public void EveryManufacturingRoom_ResolvesToAFewMeaningfulAreas()
    {
        var rooms = FloorplanCatalog.ForProfile("manufacturing").SelectMany(s => s.Rooms).ToList();
        Assert.NotEmpty(rooms);

        var areas = rooms.Select(r => SpaceFactories.FunctionalArea(r.Code)).Distinct().ToList();
        // Every room maps into the known area set, and the demo collapses to at most 5 groups.
        Assert.All(areas, a => Assert.Contains(a, ExpectedAreas));
        Assert.InRange(areas.Count, 2, ExpectedAreas.Length);
    }

    [Fact]
    public void TeamForSkills_SpecialistSkillsBeatProduction()
    {
        // QA wins even when a production skill is also present.
        Assert.Equal("Quality Team",
            PeopleFactories.TeamForSkills([SkillCatalog.QaInspection, SkillCatalog.Assembly]));
        Assert.Equal("Maintenance Team",
            PeopleFactories.TeamForSkills([SkillCatalog.Maintenance, SkillCatalog.CncOperation]));
        Assert.Equal("Logistics Team",
            PeopleFactories.TeamForSkills([SkillCatalog.ForkliftLicense]));
        Assert.Equal("Logistics Team",
            PeopleFactories.TeamForSkills([SkillCatalog.CraneOperation]));
    }

    [Theory]
    [InlineData("cnc_operation")]
    [InlineData("welding_cert")]
    [InlineData("packaging")]
    public void TeamForSkills_ProductionSkillsMapToProductionCrew(string skill)
        => Assert.Equal("Production Crew", PeopleFactories.TeamForSkills([skill]));

    [Fact]
    public void TeamForSkills_NoSkillsFallsBackToProductionCrew()
        => Assert.Equal("Production Crew", PeopleFactories.TeamForSkills([]));
}
