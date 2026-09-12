using Orkyo.Foundation.Seed.Narrative;
using Xunit;

namespace Orkyo.Foundation.Seed.Tests;

/// <summary>
/// The demo's utilization targets. These numbers are what a visitor sees on the Insights charts,
/// so they are asserted rather than left to drift: a workshop whose people sit at 5 % reads as a
/// business nobody would run, which is exactly what the old fixed-job-count seeder produced.
/// </summary>
public class DemandProfileTests
{
    [Fact]
    public void History_IsBusy_BecauseItAlreadyHappened()
    {
        Assert.Equal(DemandProfile.PeopleHistory, DemandProfile.People(-6), 3);
        Assert.Equal(DemandProfile.PeopleHistory, DemandProfile.People(-0.1), 3);
        Assert.InRange(DemandProfile.People(-6), 0.85, 0.95);
    }

    [Fact]
    public void CommittedQuarter_StaysNearCapacity()
    {
        foreach (var months in new[] { 0.0, 1.5, 2.9 })
            Assert.Equal(DemandProfile.PeopleNear, DemandProfile.People(months), 3);
    }

    [Fact]
    public void BookThins_TowardTheFarEdge()
    {
        Assert.Equal(DemandProfile.PeopleNear, DemandProfile.People(3), 3);
        Assert.Equal(DemandProfile.PeopleFar, DemandProfile.People(12), 3);
        Assert.InRange(DemandProfile.People(7.5), DemandProfile.PeopleFar, DemandProfile.PeopleNear);
        // Past the window the curve flattens rather than going negative.
        Assert.Equal(DemandProfile.PeopleFar, DemandProfile.People(24), 3);
    }

    [Fact]
    public void Curve_NeverRisesAfterTheCommittedQuarter()
    {
        var previous = DemandProfile.People(3);
        for (var m = 3.0; m <= 12.0; m += 0.25)
        {
            var current = DemandProfile.People(m);
            Assert.True(current <= previous + 1e-9, $"rose at {m} months");
            previous = current;
        }
    }

    [Fact]
    public void Stations_SitBelowPeople_SoTheShopHasSlack()
    {
        foreach (var months in new[] { -3.0, 0.0, 6.0, 12.0 })
            Assert.True(DemandProfile.Stations(months) < DemandProfile.People(months));
        // The agreed target: stations ~75 % while people run ~88 %.
        Assert.Equal(0.75, DemandProfile.Stations(-1), 2);
    }

    [Fact]
    public void Campaign_LiftsItsOwnSeason_AndNeverSaturates()
    {
        var baseline = DemandProfile.People(-1);
        Assert.True(DemandProfile.WithCampaign(baseline, inCampaignWindow: true)
            > DemandProfile.WithCampaign(baseline, inCampaignWindow: false));
        // Never 100 %: a shop with no slack cannot absorb the conflicts the demo injects on purpose.
        Assert.True(DemandProfile.WithCampaign(0.99, inCampaignWindow: true) <= DemandProfile.Cap);
    }

    [Fact]
    public void MonthsFrom_IsSignedAroundTheReferenceDate()
    {
        var reference = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(DemandProfile.MonthsFrom(reference, reference.AddDays(-90)) < 0);
        Assert.Equal(0, DemandProfile.MonthsFrom(reference, reference), 6);
        Assert.Equal(12, DemandProfile.MonthsFrom(reference, reference.AddDays(365)), 0);
    }
}
