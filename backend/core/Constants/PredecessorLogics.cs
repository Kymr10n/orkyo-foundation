namespace Api.Constants;

/// <summary>
/// Predecessor-logic string constants for database storage and SQL queries.
/// Values mirror the <see cref="Models.PredecessorLogic"/> enum's
/// <c>JsonStringEnumMemberName</c> attributes (the DB string representation) —
/// see <c>ConstantContractTests</c> for the drift guard.
/// </summary>
public static class PredecessorLogics
{
    public const string All = "all";
    public const string Any = "any";
    public const string KOfN = "k_of_n";
}
