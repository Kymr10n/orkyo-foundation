using Orkyo.Foundation.Seed.Floorplans;
using Orkyo.Foundation.Seed.Narrative;
using Xunit;

namespace Orkyo.Foundation.Seed.Tests;

/// <summary>
/// Unit guards for the narrative scaffold: every facility's job archetypes reference rooms, tools and
/// skills that actually exist, and the year calendar produces in-window, shift-aligned working slots
/// that skip holidays and shutdowns. These catch scaffold typos before any DB work.
/// </summary>
public class NarrativeScaffoldTests
{
    [Fact]
    public void SkillCatalog_KeysUnique_AndResolvable()
    {
        var keys = SkillCatalog.All.Select(s => s.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var s in SkillCatalog.All) Assert.Equal(s, SkillCatalog.ByKey(s.Key));
    }

    [Fact]
    public void Facilities_MatchFloorplanSites()
    {
        var floorplanCodes = FloorplanCatalog.ForProfile("manufacturing").Select(f => f.Code).ToHashSet();
        Assert.Equal(new[] { "PMF", "FWF", "PPF" }, FacilityModel.All.Select(f => f.SiteCode).ToArray());
        Assert.All(FacilityModel.All, f => Assert.Contains(f.SiteCode, floorplanCodes));
    }

    [Fact]
    public void EveryArchetype_ReferencesRealRoom_Tool_AndPersonSkill()
    {
        var floorplans = FloorplanCatalog.ForProfile("manufacturing");
        var personSkillKeys = SkillCatalog.All.Where(s => s.Kind == SkillKind.Person).Select(s => s.Key).ToHashSet();

        foreach (var f in FacilityModel.All)
        {
            var rooms = floorplans.First(fp => fp.Code == f.SiteCode).Rooms.Select(r => r.Code).ToHashSet();
            var toolRoles = f.Tools.Select(t => t.Role).ToHashSet();

            foreach (var room in f.ConcurrentRoomCodes)
                Assert.Contains(room, rooms);

            foreach (var a in f.Archetypes)
            {
                Assert.Contains(a.RoomCode, rooms);
                if (a.ToolRole is not null) Assert.Contains(a.ToolRole, toolRoles);
                foreach (var skill in a.RequiredSkills)
                    Assert.Contains(skill, personSkillKeys);
            }

            // The narrative needs each cadence to exist.
            Assert.Contains(f.Archetypes, a => a.Cadence == JobCadence.Campaign);
            Assert.Contains(f.Archetypes, a => a.Cadence == JobCadence.MonthlyPm);
            Assert.Contains(f.Archetypes, a => a.Cadence == JobCadence.QuarterlyQa);
        }
    }

    [Fact]
    public void Calendar_SpansTwelveMonths_AndSkipsWeekendsHolidaysShutdowns()
    {
        var cal = new YearCalendar(new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(cal.Start.AddMonths(12), cal.End);

        // A Saturday is never a working day.
        var sat = cal.MonthStarts().SelectMany(m => Enumerable.Range(0, 28).Select(i => m.AddDays(i)))
            .First(d => d.DayOfWeek == DayOfWeek.Saturday);
        Assert.False(cal.IsWorkingDay(sat));

        // Holidays and shutdown days are non-working.
        Assert.All(cal.Holidays, h => Assert.False(cal.IsWorkingDay(h.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))));
        if (cal.Shutdowns.Count > 0)
            Assert.False(cal.IsWorkingDay(cal.Shutdowns[0].Start));
    }

    [Fact]
    public void MakeSlot_StaysWithinShiftHours_AndDuration()
    {
        var cal = new YearCalendar(new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc));
        var faker = new Bogus.Faker { Random = new Bogus.Randomizer(1) };
        var day = cal.PickWorkingDay(cal.Start, cal.End, faker)!.Value;
        for (var i = 0; i < 50; i++)
        {
            var (s, e) = cal.MakeSlot(day, 2, 8, faker);
            Assert.True(s.Hour >= 6, "starts no earlier than shift A");
            Assert.True(e <= s.Date.AddHours(22), "ends no later than shift B end");
            Assert.True(e > s);
        }
    }
}
