namespace Api.Constants;

/// <summary>
/// Structured error codes returned in the body of 4xx responses.
/// The frontend switches behavior on these codes (redirect vs toast, etc.);
/// keep values in sync with <c>frontend/src/constants/api-error-codes.ts</c>.
/// </summary>
public static class ApiErrorCodes
{
    public const string SessionExpired = "session_expired";
    public const string BreakGlassExpired = "break_glass_expired";
    public const string BreakGlassHardCapReached = "break_glass_hard_cap_reached";
    public const string Forbidden = "forbidden";
    public const string TenantSuspended = "tenant_suspended";
}
