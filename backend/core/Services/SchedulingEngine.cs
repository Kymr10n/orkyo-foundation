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
    /// The last calendar day a scheduled window actually occupies.
    ///
    /// Schedule windows are half-open timestamps: a one-day placement applied on 2026-03-02 is
    /// stored as <c>[03-02 00:00, 03-03 00:00)</c>. The day-bucket consumers (the solver's fixed
    /// occupancies, dependency fold-ins, the conflict engine's precedence check, critical-path
    /// durations) all reason in INCLUSIVE last days, and a naive
    /// <c>DateOnly.FromDateTime(endTs)</c> on a midnight-exclusive end lands one day too far —
    /// phantom-occupying capacity, delaying successors, and inflating durations. An end at exactly
    /// midnight, under the half-open convention, means the window never entered that day.
    /// </summary>
    public static DateOnly InclusiveLastDay(DateTime endTs)
        => endTs.TimeOfDay == TimeSpan.Zero
            ? DateOnly.FromDateTime(endTs).AddDays(-1)
            : DateOnly.FromDateTime(endTs);

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
    /// Whole working minutes inside the UTC window <c>[fromUtc, toUtc)</c> under a
    /// site's scheduling settings.
    ///
    /// This is the capacity denominator for utilization: the share of a period a
    /// resource could actually be booked for. Null settings — or working hours off
    /// with weekends on — mean 24/7, and the raw wall-clock span is returned, which
    /// is byte-identical to the behaviour callers had before this mask existed.
    ///
    /// Off-times and absences are deliberately NOT applied here. Callers subtract
    /// blocked periods by passing each blocked overlap back through this same
    /// function, so a blocked night never subtracts capacity that was never open.
    /// </summary>
    public static double WorkingMinutesInWindow(
        DateTime fromUtc,
        DateTime toUtc,
        SchedulingSettingsInfo? settings)
    {
        if (toUtc <= fromUtc)
            return 0;

        if (settings is null || (!settings.WorkingHoursEnabled && settings.WeekendsEnabled))
            return (toUtc - fromUtc).TotalMinutes;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);

        // Walk the local calendar days that can touch the window. One day of slack on
        // each side: the local day containing fromUtc can start before it, and the one
        // containing toUtc can end after it.
        var firstDay = DateOnly.FromDateTime(ToLocal(fromUtc, tz)).AddDays(-1);
        var lastDay = DateOnly.FromDateTime(ToLocal(toUtc, tz)).AddDays(1);

        var total = 0.0;
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (!settings.WeekendsEnabled && IsWeekend(day.ToDateTime(TimeOnly.MinValue)))
                continue;

            // The day's local working window — the whole local day when only the
            // weekend rule is active.
            var localStart = settings.WorkingHoursEnabled
                ? day.ToDateTime(settings.WorkingDayStart)
                : day.ToDateTime(TimeOnly.MinValue);
            var localEnd = settings.WorkingHoursEnabled
                ? day.ToDateTime(settings.WorkingDayEnd)
                : day.AddDays(1).ToDateTime(TimeOnly.MinValue);

            if (localEnd <= localStart)
                continue;

            // Converting both edges to UTC and measuring elapsed time is what makes DST
            // correct: a 12 h local working day stays 12 h across a transition, while a
            // whole local day is 23 h or 25 h — the capacity that really exists.
            var startUtc = LocalToUtcSkippingGap(localStart, tz);
            var endUtc = LocalToUtcSkippingGap(localEnd, tz);

            var overlapStart = startUtc > fromUtc ? startUtc : fromUtc;
            var overlapEnd = endUtc < toUtc ? endUtc : toUtc;
            if (overlapEnd > overlapStart)
                total += (overlapEnd - overlapStart).TotalMinutes;
        }

        return total;
    }

    /// <summary>
    /// Local to UTC, tolerating the spring-forward gap. A local time that does not
    /// exist on a transition day (02:30 where 02:00 jumps to 03:00) moves forward to
    /// the first instant that does — the only reading of "the day starts at 02:30"
    /// that keeps a working day continuous.
    /// </summary>
    private static DateTime LocalToUtcSkippingGap(DateTime localTime, TimeZoneInfo tz)
    {
        var local = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        if (!tz.IsInvalidTime(local))
            return ToUtc(local, tz);

        // Every IANA gap is well under three hours; step in 15-minute increments to
        // cover the 30- and 45-minute historical shifts as well as the usual hour.
        for (var minutes = 15; minutes <= 180; minutes += 15)
        {
            var shifted = local.AddMinutes(minutes);
            if (!tz.IsInvalidTime(shifted))
                return ToUtc(shifted, tz);
        }

        // Unreachable for real zones; fall back to the offset rather than throw.
        return local - tz.GetUtcOffset(local);
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
