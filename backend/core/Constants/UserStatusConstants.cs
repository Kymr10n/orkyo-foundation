namespace Api.Constants;

/// <summary>
/// Global user status string constants for database storage (<c>users.status</c>)
/// and API communication. Mirror the <c>UserStatus</c> enum — parse via
/// <c>UserHelper.ParseUserStatus</c>.
/// </summary>
public static class UserStatusConstants
{
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string PendingVerification = "pending_verification";

    public static readonly IReadOnlyList<string> All =
    [
        Active,
        Disabled,
        PendingVerification,
    ];
}
