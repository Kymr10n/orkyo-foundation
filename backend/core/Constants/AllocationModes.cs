namespace Api.Constants;

public static class AllocationModes
{
    public const string Exclusive = "Exclusive";
    public const string Fractional = "Fractional";
    /// <summary>
    /// Declared, not yet implemented: every assignment on such a resource is refused
    /// (<c>ResourceAssignmentValidator</c>), so it is not in <see cref="All"/> and a resource
    /// cannot be created or switched to it. The constant stays for rows that already carry it.
    /// </summary>
    public const string ConcurrentCapacity = "ConcurrentCapacity";

    /// <summary>Every allocation mode a resource may be given, in declaration order.</summary>
    public static readonly IReadOnlyList<string> All = [Exclusive, Fractional];
}
