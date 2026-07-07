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
        List<BlockedPeriod>? offTimes)
    {
        var hasActiveOffTimes = offTimes != null && offTimes.Count > 0;

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
        var enabledOffTimes = offTimes ?? [];

        // Snap start forward if it falls outside working time
        var current = SnapToNextWorkingTime(desiredStart, settings, tz, enabledOffTimes);
        var actualStart = current;
        var remainingMinutes = requestedDurationMinutes;

        // Safety: cap iterations to prevent infinite loops from misconfiguration
        const int maxIterations = 525_600; // 1 year of minutes
        var iterations = 0;

        while (remainingMinutes > 0)
        {
            if (!IsWorkingTime(current, settings, tz, enabledOffTimes))
            {
                current = SnapToNextWorkingTime(current, settings, tz, enabledOffTimes);
                continue;
            }

            // Consume a contiguous run of working minutes in one step
            var chunkMinutes = WorkingRunMinutes(current, remainingMinutes, settings, tz, enabledOffTimes);
            if (chunkMinutes > 0)
            {
                if (iterations + (long)chunkMinutes > maxIterations)
                    throw new InvalidOperationException(
                        "Scheduling calculation exceeded maximum iterations. " +
                        "Check that working hours and off-times allow at least some working time.");
                iterations += chunkMinutes;
                current = current.AddMinutes(chunkMinutes);
                remainingMinutes -= chunkMinutes;
            }
            else
            {
                // Fallback: consume 1 minute at a time (e.g. across a DST transition)
                if (++iterations > maxIterations)
                    throw new InvalidOperationException(
                        "Scheduling calculation exceeded maximum iterations. " +
                        "Check that working hours and off-times allow at least some working time.");
                current = current.AddMinutes(1);
                remainingMinutes--;
            }

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
        List<BlockedPeriod> enabledOffTimes)
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
    public static bool IsInOffTime(DateTime utcTime, List<BlockedPeriod> offTimes)
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
        List<BlockedPeriod> enabledOffTimes)
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

    /// <summary>
    /// Computes how many whole minutes of working time can be consumed
    /// contiguously from <paramref name="fromUtc"/> (which must be a working
    /// instant) before hitting non-working time, capped at
    /// <paramref name="maxMinutes"/>. Minutes are counted on a grid anchored at
    /// <paramref name="fromUtc"/>: a minute counts iff its start instant is
    /// working, matching minute-by-minute stepping exactly. Returns 0 when a
    /// timezone offset transition falls inside the run, signalling the caller
    /// to fall back to minute-level stepping.
    /// </summary>
    private static int WorkingRunMinutes(
        DateTime fromUtc,
        int maxMinutes,
        SchedulingSettingsInfo settings,
        TimeZoneInfo tz,
        List<BlockedPeriod> enabledOffTimes)
    {
        var limit = (long)maxMinutes;

        // Off-times: first minute-grid instant that lands inside a window.
        // A window entirely between two grid instants is skipped over, exactly
        // as minute stepping would.
        foreach (var ot in enabledOffTimes)
        {
            if (ot.EndTs <= fromUtc)
                continue;
            var stepsToStart = Math.Max(0L, CeilMinutes(ot.StartTs - fromUtc));
            if (stepsToStart < limit && fromUtc.AddMinutes(stepsToStart) < ot.EndTs)
                limit = stepsToStart;
        }

        // Local-time constraints (working-day end, weekend day change) are
        // extrapolated with the current UTC offset; only valid while the
        // offset stays constant across the run.
        if (settings.WorkingHoursEnabled || !settings.WeekendsEnabled)
        {
            var offset = tz.GetUtcOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
            var local = fromUtc + offset;

            // Working-day end: first grid instant with local time-of-day >= WorkingDayEnd
            if (settings.WorkingHoursEnabled)
            {
                var dayEndUtc = local.Date.Add(settings.WorkingDayEnd.ToTimeSpan()) - offset;
                limit = Math.Min(limit, CeilMinutes(dayEndUtc - fromUtc));
            }

            // Day change: weekend status can flip at local midnight
            if (!settings.WeekendsEnabled)
            {
                var midnightUtc = local.Date.AddDays(1) - offset;
                limit = Math.Min(limit, CeilMinutes(midnightUtc - fromUtc));
            }

            var runEnd = DateTime.SpecifyKind(fromUtc.AddMinutes(limit), DateTimeKind.Utc);
            if (tz.GetUtcOffset(runEnd) != offset)
                return 0;
        }

        return (int)limit;
    }

    private static long CeilMinutes(TimeSpan span) =>
        (span.Ticks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute;

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

    public static bool IsWeekend(DateTime localTime) =>
        localTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateTime ToLocal(DateTime utcTime, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), tz);

    private static DateTime ToUtc(DateTime localTime, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), tz);
}
