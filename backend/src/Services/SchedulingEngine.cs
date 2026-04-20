using Api.Models;

namespace Api.Services;

/// <summary>
/// Pure-function scheduling engine. Single source of truth for
/// working-time arithmetic used by the API, grid rendering, and validation.
/// No state, no DI dependencies — all inputs are explicit.
/// </summary>
public static class SchedulingEngine
{
    /// <summary>
    /// Result of a scheduling calculation.
    /// </summary>
    public record ScheduleResult
    {
        public required DateTime ActualStart { get; init; }
        public required DateTime ActualEnd { get; init; }
        public required int ActualDurationMinutes { get; init; }
    }

    /// <summary>
    /// Calculates the actual end time for a request given its desired start
    /// and requested working-time duration, respecting the site's scheduling
    /// settings (working hours, weekends, off-times).
    ///
    /// If <paramref name="schedulingSettingsApply"/> is false, the result is
    /// a simple elapsed-time calculation (start + duration).
    /// </summary>
    public static ScheduleResult CalculateSchedule(
        DateTime desiredStart,
        int requestedDurationMinutes,
        bool schedulingSettingsApply,
        SchedulingSettingsInfo? settings,
        List<OffTimeInfo>? offTimes)
    {
        var hasActiveOffTimes = offTimes != null && offTimes.Count > 0 && offTimes.Any(o => o.Enabled);

        if (!schedulingSettingsApply || settings == null ||
            (!settings.WorkingHoursEnabled && !hasActiveOffTimes))
        {
            var plainEnd = desiredStart.AddMinutes(requestedDurationMinutes);
            return new ScheduleResult
            {
                ActualStart = desiredStart,
                ActualEnd = plainEnd,
                ActualDurationMinutes = requestedDurationMinutes
            };
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
        var enabledOffTimes = offTimes?.Where(o => o.Enabled).ToList() ?? [];

        // Snap start forward if it falls outside working time
        var current = SnapToNextWorkingTime(desiredStart, settings, tz, enabledOffTimes);
        var actualStart = current;
        var remainingMinutes = requestedDurationMinutes;

        // Safety: cap iterations to prevent infinite loops from misconfiguration
        const int maxIterations = 525_600; // 1 year of minutes
        var iterations = 0;

        while (remainingMinutes > 0)
        {
            if (++iterations > maxIterations)
                throw new InvalidOperationException(
                    "Scheduling calculation exceeded maximum iterations. " +
                    "Check that working hours and off-times allow at least some working time.");

            if (!IsWorkingTime(current, settings, tz, enabledOffTimes))
            {
                current = SnapToNextWorkingTime(current, settings, tz, enabledOffTimes);
                continue;
            }

            // Consume 1 minute of working time
            current = current.AddMinutes(1);
            remainingMinutes--;

            // If we've crossed into non-working time, don't count the overshoot
            if (remainingMinutes > 0 && !IsWorkingTime(current, settings, tz, enabledOffTimes))
            {
                current = SnapToNextWorkingTime(current, settings, tz, enabledOffTimes);
            }
        }

        var totalElapsedMinutes = (int)(current - actualStart).TotalMinutes;

        return new ScheduleResult
        {
            ActualStart = actualStart,
            ActualEnd = current,
            ActualDurationMinutes = totalElapsedMinutes
        };
    }

    /// <summary>
    /// Determines whether a given instant is within working time.
    /// </summary>
    public static bool IsWorkingTime(
        DateTime utcTime,
        SchedulingSettingsInfo settings,
        TimeZoneInfo tz,
        List<OffTimeInfo> enabledOffTimes)
    {
        var local = ToLocal(utcTime, tz);

        if (!settings.WeekendsEnabled && IsWeekend(local))
            return false;

        if (settings.WorkingHoursEnabled)
        {
            var timeOfDay = TimeOnly.FromDateTime(local);
            if (timeOfDay < settings.WorkingDayStart || timeOfDay >= settings.WorkingDayEnd)
                return false;
        }

        if (IsInOffTime(utcTime, enabledOffTimes))
            return false;

        return true;
    }

    /// <summary>
    /// Checks whether a UTC instant falls within any active off-time window.
    /// </summary>
    public static bool IsInOffTime(DateTime utcTime, List<OffTimeInfo> offTimes)
    {
        foreach (var ot in offTimes)
        {
            if (utcTime >= ot.StartTs && utcTime < ot.EndTs)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Snaps a time forward to the next valid working-time instant.
    /// </summary>
    internal static DateTime SnapToNextWorkingTime(
        DateTime utcTime,
        SchedulingSettingsInfo settings,
        TimeZoneInfo tz,
        List<OffTimeInfo> enabledOffTimes)
    {
        var current = utcTime;

        // Safety: max 366 days of scanning
        const int maxDays = 366;
        var startDate = current;

        while ((current - startDate).TotalDays < maxDays)
        {
            var local = ToLocal(current, tz);

            // Skip weekends
            if (!settings.WeekendsEnabled && IsWeekend(local))
            {
                var daysToMonday = local.DayOfWeek == DayOfWeek.Saturday ? 2 : 1;
                var nextMonday = local.Date.AddDays(daysToMonday)
                    .Add(settings.WorkingDayStart.ToTimeSpan());
                current = ToUtc(nextMonday, tz);
                continue;
            }

            // Before working hours — snap to start
            var timeOfDay = TimeOnly.FromDateTime(local);
            if (settings.WorkingHoursEnabled && timeOfDay < settings.WorkingDayStart)
            {
                var atStart = local.Date.Add(settings.WorkingDayStart.ToTimeSpan());
                current = ToUtc(atStart, tz);
                continue;
            }

            // After working hours — snap to next day's start
            if (settings.WorkingHoursEnabled && timeOfDay >= settings.WorkingDayEnd)
            {
                var nextDayStart = local.Date.AddDays(1).Add(settings.WorkingDayStart.ToTimeSpan());
                current = ToUtc(nextDayStart, tz);
                continue;
            }

            // Off-time check — skip to end of off-time
            var inOffTime = false;
            foreach (var ot in enabledOffTimes)
            {
                if (current >= ot.StartTs && current < ot.EndTs)
                {
                    current = ot.EndTs;
                    inOffTime = true;
                    break;
                }
            }
            if (inOffTime)
                continue;

            // We're in a valid working time slot
            return current;
        }

        throw new InvalidOperationException(
            "No working time available within 366 days. Check scheduling settings.");
    }

    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = MinutesPerHour * 24;
    private const int MinutesPerWeek = MinutesPerDay * 7;
    private const double DaysPerYear = 365.2425;              // Gregorian average (accounts for leap years)
    private const double DaysPerMonth = DaysPerYear / 12;     // ~30.44

    /// <summary>
    /// Converts a duration value + unit pair into total minutes.
    /// Uses Gregorian-average year/month lengths for leap-year accuracy.
    /// </summary>
    public static int DurationToMinutes(int value, DurationUnit unit) => unit switch
    {
        DurationUnit.Minutes => value,
        DurationUnit.Hours => value * MinutesPerHour,
        DurationUnit.Days => value * MinutesPerDay,
        DurationUnit.Weeks => value * MinutesPerWeek,
        DurationUnit.Months => (int)(value * DaysPerMonth * MinutesPerDay),
        DurationUnit.Years => (int)(value * DaysPerYear * MinutesPerDay),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown duration unit")
    };

    private static bool IsWeekend(DateTime localTime) =>
        localTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateTime ToLocal(DateTime utcTime, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), tz);

    private static DateTime ToUtc(DateTime localTime, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), tz);
}
