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
    public const string AccountLocked = "account_locked";
    public const string TenantSuspended = "tenant_suspended";
    public const string QuotaExceeded = "quota_exceeded";

    /// <summary>
    /// Error codes used by the bootstrap/auth flow.
    /// These are stable identifiers that frontends can map to user-friendly messages.
    /// </summary>
    public static class Auth
    {
        /// <summary>Keycloak identity is not linked to any user</summary>
        public const string IdentityNotLinked = "identity_not_linked";

        /// <summary>User exists but was not invited/activated</summary>
        public const string NotInvited = "not_invited";

        /// <summary>Email address not verified by identity provider</summary>
        public const string EmailNotVerified = "email_not_verified";

        /// <summary>User account is disabled or inactive</summary>
        public const string AccountInactive = "account_inactive";

        /// <summary>Invalid or missing authentication token</summary>
        public const string InvalidToken = "invalid_token";
    }
}
