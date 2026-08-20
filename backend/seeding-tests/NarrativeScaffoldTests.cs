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
    public void Calendar_SpansEighteenMonths_AndSkipsWeekendsHolidaysShutdowns()
    {
        var cal = new YearCalendar(new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc));
        // 6 months of history + 12 months ahead.
        Assert.Equal(cal.Start.AddMonths(18), cal.End);
        Assert.Equal(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), cal.Start);

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

    [Fact]
    public void EveryFacilityRoster_CoversTheWorkThatFacilityDoes()
    {
        // A persona is what makes a person's title, department, team and skills tell one story.
        // The risk of deriving skills from a role is that a role nobody staffed leaves work with
        // no qualified person — so the roster has to cover the archetypes before the seed runs.
        foreach (var facility in FacilityModel.All)
        {
            var roster = PersonaCatalog.Roster(facility.SiteCode);
            var held = roster.SelectMany(p => p.Skills).ToHashSet();

            foreach (var arch in facility.Archetypes)
            {
                // A lead holds every skill of the job they lead, so at least one persona must hold
                // the whole set rather than the set being covered between several people.
                Assert.True(
                    roster.Any(p => arch.RequiredSkills.All(p.Skills.Contains)),
                    $"{facility.SiteCode} has no persona who can lead \"{arch.Verb} {arch.Noun}\" "
                    + $"(needs {string.Join(" + ", arch.RequiredSkills)})");
            }

            foreach (var skill in FacilityModel.RequiredPersonSkills(facility))
                Assert.Contains(skill, held);
        }
    }

    [Fact]
    public void EveryPersonaJobTitle_ExistsInTheProfilePool()
    {
        // The title is resolved against the seeded job-title list by name. One that is not in the
        // pool would silently leave the person with no title at all.
        var pool = new Profiles.Manufacturing().JobTitlePool.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var site in new[] { "PMF", "FWF", "PPF" })
            foreach (var persona in PersonaCatalog.Roster(site))
                Assert.Contains(persona.JobTitle, pool);
    }

    [Fact]
    public void EveryPersonaDepartment_StartsWithAProfileRoot()
    {
        // Departments resolve child-then-root, so the first word has to be a root the profile
        // seeds; otherwise the person ends up with no department rather than a coarse one.
        var roots = new Profiles.Manufacturing().DepartmentRootPool.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var site in new[] { "PMF", "FWF", "PPF" })
            foreach (var persona in PersonaCatalog.Roster(site))
            {
                var root = persona.Department.Split(' ')[0];
                Assert.True(roots.Contains(root),
                    $"'{persona.Department}' does not start with a seeded department root");
            }
    }
}
