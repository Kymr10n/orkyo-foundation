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
    public const string CsrfTokenMismatch = "csrf_token_mismatch";
    public const string RateLimited = "rate_limited";
    public const string AccountLocked = "account_locked";
    public const string TenantSuspended = "tenant_suspended";
    public const string QuotaExceeded = "quota_exceeded";
    public const string UpgradeRequired = "upgrade_required";

    /// <summary>Resource not found (404)</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Validation error (400)</summary>
    // Value matches what has always been emitted on the wire (formerly via nameof); aligning the
    // casing to "VALIDATION_ERROR" is deferred to the next major (deliberate — breaking change).
    public const string ValidationError = "ValidationError";

    /// <summary>Conflict error (409)</summary>
    public const string Conflict = "CONFLICT";

    /// <summary>Unprocessable entity (422)</summary>
    public const string UnprocessableEntity = "UNPROCESSABLE_ENTITY";

    /// <summary>Bot-challenge verification failed (403)</summary>
    public const string ChallengeFailed = "CHALLENGE_FAILED";

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
