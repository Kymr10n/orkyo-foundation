using Api.Models;

namespace Api.Services.AutoSchedule;

/// <summary>
/// The time axis the auto-scheduler plans on: integer working minutes counted from the
/// horizon start, with every non-working stretch of the site's calendar (nights, weekends)
/// removed. A three-day job spans nights on its own, a twenty-minute operation costs twenty
/// minutes, and the solver never sees a gap it has to reason about.
///
/// Built once per run from <see cref="SchedulingEngine.WorkingSegments"/>, so the minutes
/// here are the same minutes utilization reports. The 24x7 case — settings off, or a run
/// that ignores them — is the identity mapping over the horizon.
///
/// Two snap directions at a segment boundary, because an offset on the boundary names two
/// instants: the end of one working stretch and the start of the next. A placement's start
/// snaps forward (Monday 08:00), its end snaps backward (Friday 17:00), so a job that ends
/// exactly at close of business is written as ending then, not at the next opening.
/// Every input timestamp is read at minute precision.
/// </summary>
public sealed class WorkingTimeAxis
{
    private readonly DateTime[] _segmentStart;
    private readonly DateTime[] _segmentEnd;
    private readonly int[] _offsetAtStart;

    /// <summary>Working minutes in the whole horizon; the largest valid offset.</summary>
    public int Length { get; }

    private WorkingTimeAxis(List<(DateTime StartUtc, DateTime EndUtc)> segments)
    {
        _segmentStart = new DateTime[segments.Count];
        _segmentEnd = new DateTime[segments.Count];
        _offsetAtStart = new int[segments.Count];

        var offset = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            _segmentStart[i] = segments[i].StartUtc;
            _segmentEnd[i] = segments[i].EndUtc;
            _offsetAtStart[i] = offset;
            offset += WholeMinutes(segments[i].EndUtc - segments[i].StartUtc);
        }

        Length = offset;
    }

    /// <summary>
    /// The axis for a run: the site's working segments when the run respects the settings and
    /// the settings mask anything, otherwise the identity over the horizon.
    /// </summary>
    public static WorkingTimeAxis Build(
        DateOnly horizonStart,
        DateOnly horizonEnd,
        SchedulingSettingsInfo? settings,
        bool respectSchedulingSettings)
    {
        // SchedulingValidators rejects end <= start at the boundary, so a site that reaches
        // this point in that state has corrupt stored data. Compare before subtracting:
        // TimeOnly subtraction is elapsed time and wraps at midnight, so 09:00 - 17:00 is 16
        // hours, not -8. Fail rather than silently plan against an empty working day.
        if (respectSchedulingSettings && settings is { WorkingHoursEnabled: true }
            && settings.WorkingDayEnd <= settings.WorkingDayStart)
        {
            throw new InvalidOperationException(
                $"Working hours are enabled but WorkingDayEnd ({settings.WorkingDayEnd}) "
                + $"is not after WorkingDayStart ({settings.WorkingDayStart}).");
        }

        var from = horizonStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = horizonEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var segments = SchedulingEngine
            .WorkingSegments(from, to, respectSchedulingSettings ? settings : null)
            // Boundaries at whole minutes, so an offset always names an instant exactly.
            .Select(s => (StartUtc: CeilToMinute(s.StartUtc), EndUtc: FloorToMinute(s.EndUtc)))
            .Where(s => s.EndUtc > s.StartUtc)
            .ToList();

        return new WorkingTimeAxis(segments);
    }

    /// <summary>The 24x7 axis over the horizon: offsets are plain minutes since its start.</summary>
    public static WorkingTimeAxis Identity(DateOnly horizonStart, DateOnly horizonEnd)
        => Build(horizonStart, horizonEnd, settings: null, respectSchedulingSettings: false);

    /// <summary>
    /// The offset of an instant, rounded down to the minute. An instant in a gap maps to the
    /// gap's offset — the same number as the end of the working stretch before it and the
    /// start of the one after — and anything outside the horizon clamps to its edge.
    /// </summary>
    public int ToOffset(DateTime utc)
    {
        if (_segmentStart.Length == 0) return 0;

        // First segment that ends after the instant.
        var i = FirstSegmentEndingAfter(utc);
        if (i < 0) return Length;
        if (utc <= _segmentStart[i]) return _offsetAtStart[i];

        return _offsetAtStart[i] + (int)Math.Floor((utc - _segmentStart[i]).TotalMinutes);
    }

    /// <summary>
    /// The offset of an instant that closes a range, rounded up to the minute, so a range
    /// read as <c>[ToOffset(start), ToOffsetEnd(end))</c> never covers less than it did.
    /// </summary>
    public int ToOffsetEnd(DateTime utc)
    {
        if (_segmentStart.Length == 0) return 0;

        var i = FirstSegmentEndingAfter(utc);
        if (i < 0) return Length;
        if (utc <= _segmentStart[i]) return _offsetAtStart[i];

        return _offsetAtStart[i] + (int)Math.Ceiling((utc - _segmentStart[i]).TotalMinutes);
    }

    /// <summary>The instant a placement starting at <paramref name="offset"/> begins; on a boundary, the later stretch.</summary>
    public DateTime StartAt(int offset)
    {
        ThrowIfOutside(offset);

        // Last segment whose start offset is <= offset. On a boundary that is the later
        // segment, which is the forward snap. At Length itself there is no later segment, and
        // the end of the last one is the only instant the offset can name.
        var i = LastSegmentStartingAtOrBefore(offset);
        var within = offset - _offsetAtStart[i];
        return within >= WholeMinutes(_segmentEnd[i] - _segmentStart[i])
            ? _segmentEnd[i]
            : _segmentStart[i].AddMinutes(within);
    }

    /// <summary>The instant a placement ending at <paramref name="offset"/> ends; on a boundary, the earlier stretch.</summary>
    public DateTime EndAt(int offset)
    {
        ThrowIfOutside(offset);

        // Last segment whose start offset is strictly < offset: on a boundary that is the
        // earlier segment, and the offset lands exactly on its end. Offset 0 has no earlier
        // segment; the first segment's start is the only instant it can name.
        var i = LastSegmentStartingAtOrBefore(offset);
        if (i > 0 && _offsetAtStart[i] == offset) i--;
        return _segmentStart[i].AddMinutes(offset - _offsetAtStart[i]);
    }

    private int FirstSegmentEndingAfter(DateTime utc)
    {
        var lo = 0;
        var hi = _segmentEnd.Length - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_segmentEnd[mid] > utc)
            {
                found = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return found;
    }

    private int LastSegmentStartingAtOrBefore(int offset)
    {
        var lo = 0;
        var hi = _offsetAtStart.Length - 1;
        var found = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_offsetAtStart[mid] <= offset)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }

    private void ThrowIfOutside(int offset)
    {
        if (_segmentStart.Length == 0)
            throw new InvalidOperationException("The horizon contains no working time.");
        if (offset < 0 || offset > Length)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, $"Offset must be within [0, {Length}].");
    }

    private static int WholeMinutes(TimeSpan span) => (int)(span.Ticks / TimeSpan.TicksPerMinute);

    private static DateTime FloorToMinute(DateTime t)
        => new(t.Ticks - t.Ticks % TimeSpan.TicksPerMinute, t.Kind);

    private static DateTime CeilToMinute(DateTime t)
        => t.Ticks % TimeSpan.TicksPerMinute == 0 ? t : FloorToMinute(t).AddMinutes(1);
}
