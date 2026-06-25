namespace Api.Constants;

/// <summary>
/// Planning-mode string constants for database storage and SQL queries.
/// Values mirror the <see cref="Models.PlanningMode"/> enum's
/// <c>JsonStringEnumMemberName</c> attributes (the DB string representation) —
/// see <c>ConstantContractTests</c> for the drift guard.
/// </summary>
public static class PlanningModes
{
    public const string Leaf = "leaf";
    public const string Summary = "summary";
    public const string Container = "container";
}
