namespace Api.Constants;

/// <summary>
/// Request status string constants for database storage and SQL queries.
/// Values mirror the <see cref="Models.RequestStatus"/> enum's
/// <c>JsonStringEnumMemberName</c> attributes (the DB string representation) —
/// see <c>ConstantContractTests</c> for the drift guard.
/// </summary>
public static class RequestStatuses
{
    public const string New = "new";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
    public const string Deferred = "deferred";
}
