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

    private static OffTimeInfo MakeOffTime(DateTime start, DateTime end, bool enabled = true) =>
        new()
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Title = "Off",
            Type = OffTimeType.Custom,
            AppliesToAllSpaces = true,
            StartTs = start,
            EndTs = end,
            IsRecurring = false,
            Enabled = enabled
        };

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
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
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
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
                new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        var start = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, offTimes);

        result.ActualStart.Should().Be(new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc));
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateSchedule_DisabledOffTime_IsIgnored()
    {
        var settings = MakeSettings();
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
                enabled: false)
        };

        var start = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 60, true, settings, offTimes);

        // Disabled off-time is ignored — should go straight through
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
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
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
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        SchedulingEngine.IsInOffTime(
            new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc), offTimes).Should().BeTrue();
    }

    [Fact]
    public void IsInOffTime_OutsideWindow_ReturnsFalse()
    {
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        SchedulingEngine.IsInOffTime(
            new DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc), offTimes).Should().BeFalse();
    }

    [Fact]
    public void IsInOffTime_AtExactEnd_ReturnsFalse()
    {
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
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
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
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
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
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
        var offTimes = new List<OffTimeInfo>
        {
            MakeOffTime(
                new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc)),
            MakeOffTime(
                new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc))
        };

        // Start at 08:00, request 180 min (3h)
        var start = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc);
        var result = SchedulingEngine.CalculateSchedule(start, 180, true, settings, offTimes);

        // 60 min: 08:00-09:00, skip 09:00-10:00, 60 min: 10:00-11:00, skip 11:00-12:00, 60 min: 12:00-13:00
        result.ActualEnd.Should().Be(new DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc));
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
}
