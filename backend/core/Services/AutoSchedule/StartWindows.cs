using Api.Models;

namespace Api.Services.AutoSchedule;

/// <summary>
/// Interval arithmetic over start offsets, shared by the feasibility analyzer (which
/// computes where a placement may start) and the greedy solver (which picks the earliest
/// such start that its own reservations still allow). One definition of "overlaps" for both:
/// a placement <c>[s, s+d)</c> collides with an occupancy <c>[a, b)</c> when
/// <c>s &lt; b &amp;&amp; s + d &gt; a</c>, so the starts it forbids are <c>[a-d+1, b)</c>.
/// A zero-length occupancy (<c>a == b</c>) is a point a placement may touch but not span.
/// </summary>
public static class StartWindows
{
    /// <summary>
    /// The starts in <c>[from, to)</c> that keep a placement of <paramref name="duration"/>
    /// minutes clear of every occupancy, as sorted disjoint windows.
    /// </summary>
    public static List<StartWindow> Subtract(
        int from,
        int to,
        IEnumerable<(int Start, int End)> occupancy,
        int duration)
    {
        var windows = new List<StartWindow>();
        if (to <= from) return windows;

        var forbidden = occupancy
            .Select(o => (From: o.Start - duration + 1, To: o.End))
            .Where(f => f.To > f.From)
            .OrderBy(f => f.From)
            .ToList();

        var cursor = from;
        foreach (var f in forbidden)
        {
            if (f.To <= cursor) continue;
            if (f.From >= to) break;
            if (f.From > cursor) windows.Add(new StartWindow(cursor, Math.Min(f.From, to)));
            cursor = Math.Max(cursor, f.To);
            if (cursor >= to) break;
        }
        if (cursor < to) windows.Add(new StartWindow(cursor, to));

        return windows;
    }

    /// <summary>
    /// The earliest start at or after <paramref name="bound"/> inside one of the windows that
    /// no reservation collides with, or null when there is none.
    /// </summary>
    public static int? EarliestFit(
        IReadOnlyList<StartWindow> windows,
        IReadOnlyList<(int Start, int End)> reservations,
        int duration,
        int bound)
    {
        foreach (var window in windows)
        {
            var s = Math.Max(window.From, bound);
            while (s < window.To)
            {
                var blocker = FirstCollision(reservations, s, duration);
                if (blocker is null) return s;
                // A collision means s < blocker.End, so this always moves forward.
                s = blocker.Value.End;
            }
        }
        return null;
    }

    private static (int Start, int End)? FirstCollision(
        IReadOnlyList<(int Start, int End)> reservations, int start, int duration)
    {
        foreach (var r in reservations)
        {
            if (start < r.End && start + duration > r.Start) return r;
        }
        return null;
    }
}
