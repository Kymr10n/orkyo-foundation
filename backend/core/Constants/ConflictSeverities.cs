namespace Api.Constants;

/// <summary>
/// Severity discriminators for <c>ConflictInfo.Severity</c>, the sibling of
/// <see cref="ConflictKinds"/>. The frontend keys its badge colours off these strings,
/// and <c>ConflictService</c> wrote them as bare literals while setting <c>Kind</c> from
/// a constant two lines above — the same value class, two different disciplines.
/// </summary>
public static class ConflictSeverities
{
    /// <summary>The schedule cannot stand as it is: a real clash.</summary>
    public const string Error = "error";

    /// <summary>Worth a look, but the schedule holds.</summary>
    public const string Warning = "warning";
}
