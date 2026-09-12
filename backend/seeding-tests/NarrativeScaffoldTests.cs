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
    public void Calendar_CoversTheDashboardWindow_AndSkipsWeekendsHolidaysShutdowns()
    {
        var cal = new YearCalendar(new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc));
        // 6 months of history + 12 months ahead, INCLUSIVE of the final month: the dashboard's
        // default range runs to (today + 12 months), and stopping a month short left its last
        // bucket empty so every chart ended in a cliff to zero.
        Assert.Equal(cal.Start.AddMonths(19), cal.End);
        Assert.Equal(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), cal.Start);
        Assert.True(cal.End > cal.ReferenceDate.AddMonths(12), "covers the whole default window");

        // A Saturday is never a working day.
        var sat = cal.MonthStarts().SelectMany(m => Enumerable.Range(0, 28).Select(i => m.AddDays(i)))
            .First(d => d.DayOfWeek == DayOfWeek.Saturday);
        Assert.False(cal.IsWorkingDay(sat));

        // Holidays and shutdown days are non-working.
        Assert.All(cal.Holidays, h => Assert.False(cal.IsWorkingDay(h.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))));
        if (cal.Shutdowns.Count > 0)
            Assert.False(cal.IsWorkingDay(cal.Shutdowns[0].Start));
    }

    // Slots are authored in site-local time and returned as UTC. This is the guard on the bug that
    // made the demo look idle: the shifts were UTC while the sites run Europe/Berlin 06:00-18:00,
    // so the late shift fell outside the working day — and Insights counts only the minutes inside
    // it, silently discarding about a third of every seeded person-hour. Both a summer and a winter
    // day, because the offset differs and only one of them would have caught it.
    [Theory]
    [InlineData("2026-07-15")]  // CEST, UTC+2
    [InlineData("2027-01-13")]  // CET,  UTC+1
    public void MakeSlot_StaysInsideTheSiteWorkingDay(string dayIso)
    {
        var cal = new YearCalendar(new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc));
        var faker = new Bogus.Faker { Random = new Bogus.Randomizer(1) };
        var day = DateTime.SpecifyKind(DateTime.Parse(dayIso), DateTimeKind.Utc);

        for (var i = 0; i < 100; i++)
        {
            var (s, e) = cal.MakeSlot(day, 2, 8, faker);
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(s, YearCalendar.SiteTimeZone);
            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(e, YearCalendar.SiteTimeZone);

            Assert.True(localStart.Hour >= YearCalendar.WorkDayStartHour, $"starts {localStart:HH:mm} local");
            Assert.True(localEnd.TimeOfDay <= TimeSpan.FromHours(YearCalendar.WorkDayEndHour), $"ends {localEnd:HH:mm} local");
            Assert.Equal(day.Date, localStart.Date);
            Assert.True(e > s);
        }
    }

    [Fact]
    public void WorkingWindow_IsTheSiteDay_InUtc()
    {
        var cal = new YearCalendar(new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc));
        var (s, e) = cal.WorkingWindow(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(YearCalendar.WorkDayEndHour - YearCalendar.WorkDayStartHour, (e - s).TotalHours);
        Assert.Equal(YearCalendar.WorkDayStartHour, TimeZoneInfo.ConvertTimeFromUtc(s, YearCalendar.SiteTimeZone).Hour);
    }

    [Fact]
    public void CampaignWindows_AllPeakInsideTheVisiblePartOfTheChart()
    {
        // A campaign parked eight months out is the one dense band on a chart whose window ends
        // at twelve — it reads as a lone spike in an otherwise empty year.
        var cal = new YearCalendar(new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc));
        foreach (var facility in FacilityModel.All)
        {
            var (start, end) = cal.CampaignWindow(facility.SiteCode);
            var monthsToStart = (start - cal.ReferenceDate).TotalDays / 30.4375;
            var monthsToEnd = (end - cal.ReferenceDate).TotalDays / 30.4375;
            Assert.True(monthsToStart >= -4, $"{facility.SiteCode} starts {monthsToStart:F1} months out");
            Assert.True(monthsToEnd <= 7, $"{facility.SiteCode} ends {monthsToEnd:F1} months out");
        }
    }

    [Fact]
    public void WorkingDays_AreAscending_AndAllWorkingInWindow()
    {
        var cal = new YearCalendar(new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc));
        var days = cal.WorkingDays().ToList();
        Assert.NotEmpty(days);
        Assert.All(days, d => Assert.True(cal.IsWorkingDay(d) && d >= cal.Start && d < cal.End));
        Assert.Equal(days.OrderBy(d => d).ToList(), days);
        // ~19 months of weekdays, less holidays and two shutdowns a year.
        Assert.InRange(days.Count, 340, 420);
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
