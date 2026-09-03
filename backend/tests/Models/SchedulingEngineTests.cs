using Api.Models;
using Api.Services;

namespace Api.Tests.Models;

public class SchedulingEngineTests
{
    private static SchedulingSettingsInfo MakeSettings(
        string timeZone = "UTC",
        bool workingHoursEnabled = true,
        string workingDayStart = "08:00",
        string workingDayEnd = "17:00",
        bool weekendsEnabled = true,
        bool publicHolidaysEnabled = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            TimeZone = timeZone,
            WorkingHoursEnabled = workingHoursEnabled,
            WorkingDayStart = TimeOnly.Parse(workingDayStart),
            WorkingDayEnd = TimeOnly.Parse(workingDayEnd),
            WeekendsEnabled = weekendsEnabled,
            PublicHolidaysEnabled = publicHolidaysEnabled
        };

    private static BlockedPeriod MakeBlockedPeriod(DateTime start, DateTime end) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = "Off",
            StartTs = start,
            EndTs = end,
            Source = BlockedPeriodSource.ResourceAbsence,
            AbsenceType = AbsenceType.Custom
        };

    // ── Working minutes in a window (the utilization capacity denominator) ──

    [Fact]
    public void WorkingMinutesInWindow_NullSettings_ReturnsWallClockSpan()
    {
        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.WorkingMinutesInWindow(from, from.AddDays(1), null)
            .Should().Be(1440);
    }

    [Fact]
    public void WorkingMinutesInWindow_WorkingHoursOffAndWeekendsOn_ReturnsWallClockSpan()
    {
        // The 24/7 case every unconfigured site is in: capacity must stay exactly what it
        // was before the mask existed, or every such tenant's figures move for no reason.
        var settings = MakeSettings(workingHoursEnabled: false, weekendsEnabled: true);
        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.WorkingMinutesInWindow(from, from.AddDays(7), settings)
            .Should().Be(7 * 1440);
    }

    [Fact]
    public void WorkingMinutesInWindow_EmptyOrInvertedWindow_ReturnsZero()
    {
        var at = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.WorkingMinutesInWindow(at, at, MakeSettings()).Should().Be(0);
        SchedulingEngine.WorkingMinutesInWindow(at, at.AddHours(-1), MakeSettings()).Should().Be(0);
    }

    [Fact]
    public void WorkingMinutesInWindow_BerlinWeekdayShift_CountsOnlyWorkingHours()
    {
        // The demo tenant's shape: Europe/Berlin, 06:00-18:00, weekends off.
        var settings = MakeSettings("Europe/Berlin", workingDayStart: "06:00",
            workingDayEnd: "18:00", weekendsEnabled: false);

        // Monday 2026-04-06 00:00 Berlin (= 04-05 22:00 UTC) through the following Monday.
        var from = new DateTime(2026, 4, 5, 22, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.WorkingMinutesInWindow(from, from.AddDays(7), settings)
            .Should().Be(5 * 12 * 60);
    }

    [Fact]
    public void WorkingMinutesInWindow_WeekendBucket_ReturnsZero()
    {
        var settings = MakeSettings("Europe/Berlin", workingDayStart: "06:00",
            workingDayEnd: "18:00", weekendsEnabled: false);

        // Saturday 2026-04-11 00:00 Berlin through Monday 00:00 Berlin.
        var from = new DateTime(2026, 4, 10, 22, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.WorkingMinutesInWindow(from, from.AddDays(2), settings).Should().Be(0);
    }

    [Fact]
    public void WorkingMinutesInWindow_WeekendsOffOnly_CountsWholeWeekdays()
    {
        var settings = MakeSettings("Europe/Berlin", workingHoursEnabled: false, weekendsEnabled: false);
        var from = new DateTime(2026, 4, 5, 22, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.WorkingMinutesInWindow(from, from.AddDays(7), settings)
            .Should().Be(5 * 1440);
    }

    [Fact]
    public void WorkingMinutesInWindow_SpringForwardDay_KeepsWorkingDayLength()
    {
        // 2026-03-29 Berlin loses 02:00-03:00. A 06:00-18:00 day is untouched by the gap,
        // so the working day is still 12 h even though the calendar day is 23 h.
        var settings = MakeSettings("Europe/Berlin", workingDayStart: "06:00", workingDayEnd: "18:00");
        var from = new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.WorkingMinutesInWindow(from, from.AddDays(1), settings).Should().Be(12 * 60);
    }

    [Fact]
    public void WorkingMinutesInWindow_FallBackDay_KeepsWorkingDayLength()
    {
        // 2026-10-25 Berlin repeats 02:00-03:00; the 06:00-18:00 window is again unaffected.
        var settings = MakeSettings("Europe/Berlin", workingDayStart: "06:00", workingDayEnd: "18:00");
        var from = new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.WorkingMinutesInWindow(from, from.AddDays(1), settings).Should().Be(12 * 60);
    }

    [Fact]
    public void WorkingMinutesInWindow_WholeDayAcrossTransition_ReflectsRealElapsedTime()
    {
        // With no working-hours limit the day itself is the capacity, so the transition days
        // really are 23 h and 25 h of elapsed availability.
        var settings = MakeSettings("Europe/Berlin", workingHoursEnabled: false, weekendsEnabled: false);

        // 24 UTC hours from Sunday 00:00 Berlin land at Monday 01:00 Berlin, because the
        // Sunday lost an hour — so the window covers no Sunday capacity (weekends are off)
        // and the first 60 minutes of Monday. Counting local days without converting the
        // edges to UTC would have reported 0 here.
        var springFrom = new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc);   // Sun 03-29 00:00 local
        SchedulingEngine.WorkingMinutesInWindow(springFrom, springFrom.AddDays(1), settings)
            .Should().Be(60);

        // A window aligned to the local Monday is a full day either side of a transition.
        var mondayFrom = new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc);   // Mon 03-30 00:00 local
        SchedulingEngine.WorkingMinutesInWindow(mondayFrom, mondayFrom.AddDays(1), settings)
            .Should().Be(1440);
    }

    [Fact]
    public void WorkingMinutesInWindow_PartialDay_ClampsToTheWindow()
    {
        var settings = MakeSettings("Europe/Berlin", workingDayStart: "06:00", workingDayEnd: "18:00");

        // Wednesday 2026-04-08, 08:00-12:00 Berlin (06:00-10:00 UTC) — fully inside the shift.
        var from = new DateTime(2026, 4, 8, 6, 0, 0, DateTimeKind.Utc);
        SchedulingEngine.WorkingMinutesInWindow(from, from.AddHours(4), settings).Should().Be(240);

        // A window straddling the end of the shift counts only the part before it.
        var late = new DateTime(2026, 4, 8, 14, 0, 0, DateTimeKind.Utc);  // 16:00 Berlin
        SchedulingEngine.WorkingMinutesInWindow(late, late.AddHours(4), settings).Should().Be(120);
    }

    [Fact]
    public void WorkingMinutesInWindow_UnresolvableTimeZone_Throws()
    {
        // The contract AppExceptionHandler's TimeZoneNotFoundException arm relies on.
        var settings = MakeSettings("Not/AZone");
        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var act = () => SchedulingEngine.WorkingMinutesInWindow(from, from.AddDays(1), settings);

        act.Should().Throw<TimeZoneNotFoundException>();
    }

    // ── Plain elapsed time (no scheduling settings) ─────────────────

    [Fact]
    public void CalculateSchedule_WithoutSettings_ReturnsPlainElapsedTime()
    {
        var start = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 120, false, null, null);

        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(start.AddMinutes(120));
        result.ActualDurationMinutes.Should().Be(120);
    }

    [Fact]
    public void CalculateSchedule_SettingsApplyButNull_ReturnsPlainElapsedTime()
    {
        var start = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, null, null);

        result.ActualEnd.Should().Be(start.AddMinutes(60));
    }

    [Fact]
    public void CalculateSchedule_WorkingHoursDisabled_ReturnsPlainElapsedTime()
    {
        var settings = MakeSettings(workingHoursEnabled: false);
        var start = new DateTime(2026, 4, 1, 22, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, null);

        result.ActualEnd.Should().Be(start.AddMinutes(60));
    }

    // ── Working hours ───────────────────────────────────────────────

    [Fact]
    public void CalculateSchedule_WithinWorkingHours_ConsumesDirectly()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        // Wednesday 2026-04-01 10:00 UTC
        var start = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, []);

        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(start.AddMinutes(60));
        result.ActualDurationMinutes.Should().Be(60);
    }

    [Fact]
    public void CalculateSchedule_SpansAcrossWorkingDayEnd_SkipsNighttime()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        // Wednesday 2026-04-01 16:00 UTC — 60 min left in day, requesting 120min
        var start = new DateTime(2026, 4, 1, 16, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 120, true, settings, []);

        result.ActualStart.Should().Be(start);
        // 60 min consumed on day 1 (16:00-17:00), then 60 min on day 2 (08:00-09:00)
        var expectedEnd = new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc);
        result.ActualEnd.Should().Be(expectedEnd);
    }

    [Fact]
    public void CalculateSchedule_StartBeforeWorkingHours_SnapsForward()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        // Wednesday 2026-04-01 05:00 UTC — before working hours
        var start = new DateTime(2026, 4, 1, 5, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, []);

        // Should snap to 08:00
        result.ActualStart.Should().Be(new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc));
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_StartAfterWorkingHours_SnapsToNextDay()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        // Wednesday 2026-04-01 20:00 UTC — after working hours
        var start = new DateTime(2026, 4, 1, 20, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, []);

        result.ActualStart.Should().Be(new DateTime(2026, 4, 2, 8, 0, 0, DateTimeKind.Utc));
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc));
    }

    // ── Weekends ────────────────────────────────────────────────────

    [Fact]
    public void CalculateSchedule_WeekendsDisabled_SkipsSaturdayAndSunday()
    {
        var settings = MakeSettings(weekendsEnabled: false); // 08:00-17:00, no weekends
        // Friday 2026-04-03 16:00 UTC — 60 min left, requesting 120
        var start = new DateTime(2026, 4, 3, 16, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 120, true, settings, []);

        // 60 min Friday 16:00-17:00, skip Sat+Sun, 60 min Monday 08:00-09:00
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 6, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_StartOnSaturday_SnapsToMonday()
    {
        var settings = MakeSettings(weekendsEnabled: false);
        // Saturday 2026-04-04 10:00 UTC
        var start = new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, []);

        result.ActualStart.Should().Be(new DateTime(2026, 4, 6, 8, 0, 0, DateTimeKind.Utc));
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 6, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_WeekendsEnabled_WorksThroughWeekend()
    {
        var settings = MakeSettings(weekendsEnabled: true);
        // Saturday 2026-04-04 10:00 UTC
        var start = new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, []);

        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(start.AddMinutes(60));
    }

    // ── Off-times ───────────────────────────────────────────────────

    [Fact]
    public void CalculateSchedule_HitsOffTime_SkipsToEnd()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        // Off-time from 10:00 to 12:00
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        // Start at 09:00, request 120 min
        var start = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 120, true, settings, offTimes);

        // 60 min: 09:00-10:00, then skip 10:00-12:00, then 60 min: 12:00-13:00
        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_StartInOffTime_SnapsToEndOfOffTime()
    {
        var settings = MakeSettings();
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        var start = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, offTimes);

        result.ActualStart.Should().Be(new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc));
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_EmptyBlockedPeriods_RunsStraightThrough()
    {
        var settings = MakeSettings();
        // Resolver pre-filters disabled records — engine receives an empty list.
        var offTimes = new List<BlockedPeriod>();

        var start = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, offTimes);

        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(start.AddMinutes(60));
    }

    // ── Timezone ────────────────────────────────────────────────────

    [Fact]
    public void CalculateSchedule_RespectsTimezone()
    {
        // Working hours 08:00-17:00 in Europe/Berlin (UTC+2 in summer)
        var settings = MakeSettings(timeZone: "Europe/Berlin");
        // 05:00 UTC = 07:00 Berlin → before working hours
        var start = new DateTime(2026, 4, 1, 5, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, []);

        // Should snap to 08:00 Berlin = 06:00 UTC
        result.ActualStart.Should().Be(new DateTime(2026, 4, 1, 6, 0, 0, DateTimeKind.Utc));
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 7, 0, 0, DateTimeKind.Utc));
    }

    // ── IsWorkingTime ───────────────────────────────────────────────

    [Fact]
    public void IsWorkingTime_DuringWorkingHours_ReturnsTrue()
    {
        var settings = MakeSettings();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("UTC");
        var time = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.IsWorkingTime(time, settings, tz, []).Should().BeTrue();
    }

    [Fact]
    public void IsWorkingTime_OutsideWorkingHours_ReturnsFalse()
    {
        var settings = MakeSettings();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("UTC");
        var time = new DateTime(2026, 4, 1, 20, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.IsWorkingTime(time, settings, tz, []).Should().BeFalse();
    }

    [Fact]
    public void IsWorkingTime_OnWeekendWithWeekendsDisabled_ReturnsFalse()
    {
        var settings = MakeSettings(weekendsEnabled: false);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("UTC");
        // Saturday 2026-04-04 10:00 UTC
        var time = new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.IsWorkingTime(time, settings, tz, []).Should().BeFalse();
    }

    [Fact]
    public void IsWorkingTime_InOffTime_ReturnsFalse()
    {
        var settings = MakeSettings();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("UTC");
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };
        var time = new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc);

        SchedulingEngine.IsWorkingTime(time, settings, tz, offTimes).Should().BeFalse();
    }

    // ── IsInOffTime ─────────────────────────────────────────────────

    [Fact]
    public void IsInOffTime_WithinWindow_ReturnsTrue()
    {
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        SchedulingEngine.IsInOffTime(
            new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc), offTimes).Should().BeTrue();
    }

    [Fact]
    public void IsInOffTime_OutsideWindow_ReturnsFalse()
    {
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        SchedulingEngine.IsInOffTime(
            new DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc), offTimes).Should().BeFalse();
    }

    [Fact]
    public void IsInOffTime_AtExactEnd_ReturnsFalse()
    {
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        // End is exclusive
        SchedulingEngine.IsInOffTime(
            new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc), offTimes).Should().BeFalse();
    }

    // ── Edge cases ──────────────────────────────────────────────────

    [Fact]
    public void CalculateSchedule_ZeroDuration_ReturnsSameStartAndEnd()
    {
        var settings = MakeSettings();
        var start = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 0, true, settings, []);

        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(start);
        result.ActualDurationMinutes.Should().Be(0);
    }

    // ── Working hours disabled + off-times ─────────────────────────

    [Fact]
    public void CalculateSchedule_WorkingHoursDisabledWithOffTimes_StillSkipsOffTimes()
    {
        var settings = MakeSettings(workingHoursEnabled: false);
        // Off-time from 10:00 to 14:00
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 14, 0, 0, DateTimeKind.Utc))
        };

        // Start at 09:00, request 120 min
        var start = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 120, true, settings, offTimes);

        // 60 min: 09:00-10:00, skip 10:00-14:00 (off-time), then 60 min: 14:00-15:00
        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 15, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_WorkingHoursDisabledStartInOffTime_SnapsForward()
    {
        var settings = MakeSettings(workingHoursEnabled: false);
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        var start = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, offTimes);

        result.ActualStart.Should().Be(new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc));
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_WorkingHoursDisabledNoOffTimes_PlainElapsed()
    {
        var settings = MakeSettings(workingHoursEnabled: false);
        // Midnight on a Saturday — should still work because weekends + hours both disabled in effect
        var start = new DateTime(2026, 4, 4, 3, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 120, true, settings, []);

        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(start.AddMinutes(120));
    }

    // ── Multiple off-times ──────────────────────────────────────────

    [Fact]
    public void CalculateSchedule_MultipleOffTimes_SkipsAll()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc)),
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        // Start at 08:00, request 180 min (3h)
        var start = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 180, true, settings, offTimes);

        // 60 min: 08:00-09:00, skip 09:00-10:00, 60 min: 10:00-11:00, skip 11:00-12:00, 60 min: 12:00-13:00
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc));
    }

    // ── Multi-day spans ─────────────────────────────────────────────

    [Fact]
    public void CalculateSchedule_SpansMultipleDays_ConsumesWholeWorkingDays()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC → 540 min/day
        // Wednesday 2026-04-01 08:00 UTC, request 3 working days + 2h
        var start = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 3 * 540 + 120, true, settings, []);

        result.ActualStart.Should().Be(start);
        // Full days Wed/Thu/Fri, then 120 min Saturday 08:00-10:00 (weekends enabled)
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_OffTimeSpansDayBoundary_SkipsAcrossDays()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        // Off-time from Wed 15:00 to Thu 10:00
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 15, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc))
        };

        // Start Wed 14:00, request 120 min
        var start = new DateTime(2026, 4, 1, 14, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 120, true, settings, offTimes);

        // 60 min Wed 14:00-15:00, skip to Thu 10:00, 60 min Thu 10:00-11:00
        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 2, 11, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_OffTimeOverlapsWorkingDayEnd_ResumesNextMorning()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        // Off-time 16:00-20:00 partially overlaps the working window
        var offTimes = new List<BlockedPeriod>
        {
            MakeBlockedPeriod(
                new DateTime(2026, 4, 1, 16, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 20, 0, 0, DateTimeKind.Utc))
        };

        // Start Wed 15:00, request 120 min
        var start = new DateTime(2026, 4, 1, 15, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 120, true, settings, offTimes);

        // 60 min Wed 15:00-16:00, off-time ends 20:00 (after hours), 60 min Thu 08:00-09:00
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_EndsExactlyAtWorkingDayEnd_DoesNotSnapToNextDay()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        // Wednesday 2026-04-01 16:00 UTC, request exactly the 60 min left in the day
        var start = new DateTime(2026, 4, 1, 16, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, []);

        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 17, 0, 0, DateTimeKind.Utc));
        result.ActualDurationMinutes.Should().Be(60);
    }

    [Fact]
    public void CalculateSchedule_NonMinuteAlignedStart_CountsMinutesOnStartGrid()
    {
        var settings = MakeSettings(); // 08:00-17:00 UTC
        // Start at 16:30:30 — the minute grid is anchored at the start instant
        var start = new DateTime(2026, 4, 1, 16, 30, 30, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, []);

        // 30 grid minutes fit before 17:00 (16:30:30 .. 16:59:30), the instant
        // 17:00:30 is outside working hours → remaining 30 min on day 2
        result.ActualStart.Should().Be(start);
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 2, 8, 30, 0, DateTimeKind.Utc));
    }

    // ── Equivalence with minute-stepping reference ──────────────────

    [Fact]
    public void CalculateSchedule_MatchesMinuteSteppingReference_AcrossRandomizedInputs()
    {
        // Deterministic randomized grid comparing the chunked engine against a
        // verbatim copy of the original minute-stepping algorithm.
        var rng = new Random(20260707);
        var baseDate = new DateTime(2026, 3, 23, 0, 0, 0, DateTimeKind.Utc); // Monday

        for (var i = 0; i < 300; i++)
        {
            var settings = MakeSettings(
                timeZone: rng.Next(2) == 0 ? "UTC" : "Europe/Berlin",
                workingHoursEnabled: rng.Next(4) != 0,
                workingDayStart: rng.Next(2) == 0 ? "08:00" : "09:30",
                workingDayEnd: rng.Next(2) == 0 ? "17:00" : "16:15",
                weekendsEnabled: rng.Next(2) == 0);

            var offTimes = new List<BlockedPeriod>();
            var offTimeCount = rng.Next(4);
            for (var j = 0; j < offTimeCount; j++)
            {
                var offStart = baseDate
                    .AddMinutes(rng.Next(14 * 24 * 60))
                    .AddSeconds(rng.Next(60));
                offTimes.Add(MakeBlockedPeriod(offStart, offStart.AddMinutes(rng.Next(1, 2000))));
            }

            var start = baseDate
                .AddMinutes(rng.Next(10 * 24 * 60))
                .AddSeconds(rng.Next(60));
            var duration = rng.Next(0, 3000);

            var expected = MinuteSteppingReference(start, duration, settings, offTimes);
            var actual = SchedulingEngine.CalculateSchedule(start, duration, true, settings, offTimes);

            actual.Should().Be(expected,
                $"case {i}: start={start:O}, duration={duration}, tz={settings.TimeZone}, " +
                $"wh={settings.WorkingHoursEnabled} {settings.WorkingDayStart}-{settings.WorkingDayEnd}, " +
                $"weekends={settings.WeekendsEnabled}, offTimes={offTimes.Count}");
        }
    }

    /// <summary>
    /// Verbatim copy of the original O(duration) minute-stepping algorithm,
    /// kept as the semantic reference for the chunked implementation.
    /// </summary>
    private static SchedulingEngine.ScheduleResult MinuteSteppingReference(
        DateTime desiredStart,
        int requestedDurationMinutes,
        SchedulingSettingsInfo settings,
        List<BlockedPeriod> offTimes)
    {
        if (!settings.WorkingHoursEnabled && offTimes.Count == 0)
        {
            return new SchedulingEngine.ScheduleResult
            {
                ActualStart = desiredStart,
                ActualEnd = desiredStart.AddMinutes(requestedDurationMinutes),
                ActualDurationMinutes = requestedDurationMinutes
            };
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);

        var current = SchedulingEngine.SnapToNextWorkingTime(desiredStart, settings, tz, offTimes);
        var actualStart = current;
        var remainingMinutes = requestedDurationMinutes;

        while (remainingMinutes > 0)
        {
            if (!SchedulingEngine.IsWorkingTime(current, settings, tz, offTimes))
            {
                current = SchedulingEngine.SnapToNextWorkingTime(current, settings, tz, offTimes);
                continue;
            }

            current = current.AddMinutes(1);
            remainingMinutes--;

            if (remainingMinutes > 0 && !SchedulingEngine.IsWorkingTime(current, settings, tz, offTimes))
            {
                current = SchedulingEngine.SnapToNextWorkingTime(current, settings, tz, offTimes);
            }
        }

        return new SchedulingEngine.ScheduleResult
        {
            ActualStart = actualStart,
            ActualEnd = current,
            ActualDurationMinutes = (int)(current - actualStart).TotalMinutes
        };
    }

    // ── DurationToMinutes ───────────────────────────────────────────

    [Theory]
    [InlineData(30, DurationUnit.Minutes, 30)]
    [InlineData(2, DurationUnit.Hours, 120)]
    [InlineData(4, DurationUnit.Days, 5760)]
    [InlineData(1, DurationUnit.Weeks, 10080)]
    [InlineData(1, DurationUnit.Months, 43829)]    // 30.4369 days × 1440 min
    [InlineData(1, DurationUnit.Years, 525949)]     // 365.2425 days × 1440 min
    public void DurationToMinutes_ConvertsCorrectly(int value, DurationUnit unit, int expectedMinutes)
    {
        SchedulingEngine.DurationToMinutes(value, unit).Should().Be(expectedMinutes);
    }

    // ── InclusiveLastDay ──────────────────────────────────────────────────────
    // Schedule windows are half-open; every day-bucket consumer (fixed occupancies,
    // dependency fold-ins, precedence conflicts, critical-path durations) reasons in
    // inclusive last days. These pin the conversion both ways.

    [Fact]
    public void InclusiveLastDay_MidnightEnd_IsThePreviousDay()
    {
        // A one-day placement applied on 03-02 stores end_ts 03-03T00:00. Its last occupied
        // day is 03-02 — reading 03-03 phantom-occupied a day per applied placement.
        var lastDay = SchedulingEngine.InclusiveLastDay(new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc));

        lastDay.Should().Be(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public void InclusiveLastDay_MidDayEnd_IsThatDay()
    {
        // A manually scheduled 09:00–17:00 window genuinely occupies its end date.
        var lastDay = SchedulingEngine.InclusiveLastDay(new DateTime(2026, 3, 2, 17, 0, 0, DateTimeKind.Utc));

        lastDay.Should().Be(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public void InclusiveLastDay_OneSecondPastMidnight_IsThatDay()
    {
        // The exclusion applies to exactly midnight only — any entry into the day counts.
        var lastDay = SchedulingEngine.InclusiveLastDay(new DateTime(2026, 3, 3, 0, 0, 1, DateTimeKind.Utc));

        lastDay.Should().Be(new DateOnly(2026, 3, 3));
    }
}
