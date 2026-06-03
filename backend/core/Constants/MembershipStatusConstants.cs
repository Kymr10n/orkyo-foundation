namespace Api.Constants;

/// <summary>
/// Membership status string constants for database storage and API communication.
/// Correspond to the <c>tenant_memberships_status_check</c> constraint:
/// <c>('active', 'pending', 'disabled')</c>.
/// </summary>
public static class MembershipStatusConstants
{
    public const string Active = "active";
    public const string Pending = "pending";
    public const string Disabled = "disabled";
}
