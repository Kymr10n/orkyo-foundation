using Api.Models;

namespace Api.Services;

/// <summary>
/// One resource's assignments, arranged so that "which of these overlap this window?" does not
/// have to read all of them.
/// </summary>
/// <remarks>
/// The batch validator asks that question once per assignment it validates, against every
/// assignment the same resource holds. Scanning the list each time is quadratic per resource, and
/// the cost is real rather than theoretical: on a seeded demo year — 20,099 assignments over 640
/// resources, the busiest holding 528 — it worked out at roughly 2.5 million comparisons and about
/// a second of CPU, while the queries feeding it took under 25 milliseconds in total.
/// <para>
/// Sorting by start is not enough on its own: a long assignment that began well before the window
/// still overlaps it, so a backward scan cannot stop at the first non-overlapping neighbour.
/// <see cref="_maxEndSoFar"/> is the running maximum end time up to each position, which gives a
/// sound stopping rule — once it is at or before the window's start, nothing earlier can reach
/// into the window and the scan is finished.
/// </para>
/// </remarks>
public sealed class AssignmentOverlapIndex
{
    private readonly ResourceAssignmentInfo[] _byStart;
    private readonly DateTime[] _maxEndSoFar;

    public AssignmentOverlapIndex(IEnumerable<ResourceAssignmentInfo> assignments)
    {
        _byStart = assignments.OrderBy(a => a.StartUtc).ToArray();
        _maxEndSoFar = new DateTime[_byStart.Length];

        var max = DateTime.MinValue;
        for (var i = 0; i < _byStart.Length; i++)
        {
            if (_byStart[i].EndUtc > max) max = _byStart[i].EndUtc;
            _maxEndSoFar[i] = max;
        }
    }

    public int Count => _byStart.Length;

    /// <summary>
    /// The assignments overlapping [<paramref name="start"/>, <paramref name="end"/>), excluding
    /// <paramref name="excludeId"/>. Half-open on both sides, matching the validator's own rule:
    /// touching at an endpoint is not an overlap.
    /// </summary>
    public List<ResourceAssignmentInfo> Overlapping(DateTime start, DateTime end, Guid? excludeId)
    {
        var found = new List<ResourceAssignmentInfo>();
        if (_byStart.Length == 0) return found;

        // Everything from here on starts at or after the window ends, so it cannot overlap.
        for (var i = FirstStartingAtOrAfter(end) - 1; i >= 0; i--)
        {
            // Nothing at or before this position reaches into the window.
            if (_maxEndSoFar[i] <= start) break;

            var candidate = _byStart[i];
            if (candidate.EndUtc <= start) continue;
            if (excludeId is not null && candidate.Id == excludeId) continue;
            found.Add(candidate);
        }

        return found;
    }

    /// <summary>Index of the first assignment starting at or after <paramref name="moment"/>.</summary>
    private int FirstStartingAtOrAfter(DateTime moment)
    {
        int low = 0, high = _byStart.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (_byStart[mid].StartUtc < moment) low = mid + 1;
            else high = mid;
        }
        return low;
    }
}
