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
    {
        var problem = new AuthProblemDetails
        {
            Type = $"https://orkyo.app/problems/{code}",
            Title = title,
            Detail = detail,
            Status = statusCode,
            Code = code
        };

        return Results.Json(problem, statusCode: statusCode);
    }

    /// <summary>
    /// Extended problem details with error code.
    /// </summary>
    public class AuthProblemDetails
    {
        /// <summary>URI reference identifying the problem type</summary>
        public string? Type { get; init; }

        /// <summary>Short human-readable summary</summary>
        public required string Title { get; init; }

        /// <summary>Human-readable explanation</summary>
        public string? Detail { get; init; }

        /// <summary>HTTP status code</summary>
        public int Status { get; init; }

        /// <summary>Machine-readable error code for frontend mapping</summary>
        public required string Code { get; init; }
    }
}
