using Api.Constants;

namespace Api.Helpers;

/// <summary>
/// RFC 7807 ProblemDetails response helper.
/// Creates standardized error responses for API consumers.
/// </summary>
public static class ProblemDetailsHelper
{
    /// <summary>
    /// Error codes used by the bootstrap/auth flow.
    /// Delegates to <see cref="ApiErrorCodes.Auth"/> (defined in Core).
    /// </summary>
    public static class AuthCodes
    {
        public const string IdentityNotLinked = ApiErrorCodes.Auth.IdentityNotLinked;
        public const string NotInvited = ApiErrorCodes.Auth.NotInvited;
        public const string EmailNotVerified = ApiErrorCodes.Auth.EmailNotVerified;
        public const string AccountInactive = ApiErrorCodes.Auth.AccountInactive;
        public const string InvalidToken = ApiErrorCodes.Auth.InvalidToken;
    }

    /// <summary>
    /// Create a problem details result for authentication errors.
    /// </summary>
    /// <param name="code">Stable error code (use AuthCodes constants)</param>
    /// <param name="title">Human-readable summary</param>
    /// <param name="detail">Detailed explanation (optional)</param>
    /// <param name="statusCode">HTTP status code (default 400)</param>
    public static IResult AuthProblem(
        string code,
        string title,
        string? detail = null,
        int statusCode = 400)
        // Routed through the canonical builder: AuthProblemDetails carried the same five
        // fields and the same type URI, and was the last shape still emitted without the
        // application/problem+json content type. Same JSON out, correct content type in.
        => ProblemResults.Problem(statusCode, code, detail: detail, title: title);
}
