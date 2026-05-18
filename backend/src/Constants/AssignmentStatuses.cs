namespace Api.Constants;

public static class AssignmentStatuses
{
    public const string Planned = "Planned";
    public const string Confirmed = "Confirmed";
    public const string Tentative = "Tentative";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlySet<string> Active =
        new HashSet<string> { Planned, Confirmed, Tentative };
}
