using Api.Constants;
using Api.Models;

namespace Api.Services;

/// <summary>
/// The one load rule every limit check uses (docs/scheduling-integrity-spec.md, section 2).
/// A booking carries a unit, a resource has a limit, and a check compares the peak of the units
/// at any one instant with the limit:
/// <list type="bullet">
/// <item><c>Exclusive</c>: unit 1, limit 1.</item>
/// <item><c>Fractional</c>: unit = the percent (empty counts as the whole resource, 100),
/// limit = the base availability percent.</item>
/// <item><c>ConcurrentCapacity</c>: unit = the units (empty counts as 1), limit = the capacity.</item>
/// </list>
/// The peak, not the sum: two bookings that never run at the same time never add up.
/// </summary>
public static class ResourceLoad
{
    public static decimal Unit(string mode, decimal? percent, int? units) => mode switch
    {
        AllocationModes.Fractional => percent ?? 100m,
        AllocationModes.ConcurrentCapacity => units ?? 1,
        _ => 1,
    };

    public static decimal Limit(string mode, ResourceInfo resource) => mode switch
    {
        AllocationModes.Fractional => resource.BaseAvailabilityPercent,
        AllocationModes.ConcurrentCapacity => resource.Capacity,
        _ => 1,
    };

    /// <summary>
    /// The highest sum of units at one instant inside <c>[from, to)</c>. Intervals are half-open:
    /// a booking that ends when another starts does not overlap it.
    /// </summary>
    public static decimal Peak(IEnumerable<(DateTime Start, DateTime End, decimal Unit)> bookings, DateTime from, DateTime to)
    {
        var edges = new List<(DateTime At, decimal Delta)>();
        foreach (var (start, end, unit) in bookings)
        {
            var s = start > from ? start : from;
            var e = end < to ? end : to;
            if (s >= e) continue;
            edges.Add((s, unit));
            edges.Add((e, -unit));
        }

        // At one instant the ends come first (negative delta), so touching bookings never stack.
        edges.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Delta.CompareTo(b.Delta));

        decimal load = 0, peak = 0;
        foreach (var (_, delta) in edges)
        {
            load += delta;
            if (load > peak) peak = load;
        }
        return peak;
    }
}
