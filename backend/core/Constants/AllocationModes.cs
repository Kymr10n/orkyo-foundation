namespace Api.Constants;

public static class AllocationModes
{
    public const string Exclusive = "Exclusive";
    public const string Fractional = "Fractional";
    public const string ConcurrentCapacity = "ConcurrentCapacity";

    /// <summary>Every allocation mode, in declaration order.</summary>
    public static readonly IReadOnlyList<string> All = [Exclusive, Fractional, ConcurrentCapacity];
}
