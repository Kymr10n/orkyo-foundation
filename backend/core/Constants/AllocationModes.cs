namespace Api.Constants;

public static class AllocationModes
{
    public const string Exclusive = "Exclusive";
    public const string Fractional = "Fractional";
    /// <summary>
    /// Up to <c>capacity</c> bookings at the same instant (each counts its units, else 1); only on
    /// placeable types, which are the ones with a capacity. See <c>ResourceLoad</c>.
    /// </summary>
    public const string ConcurrentCapacity = "ConcurrentCapacity";

    /// <summary>Every allocation mode a resource may be given, in declaration order.</summary>
    public static readonly IReadOnlyList<string> All = [Exclusive, Fractional, ConcurrentCapacity];
}
