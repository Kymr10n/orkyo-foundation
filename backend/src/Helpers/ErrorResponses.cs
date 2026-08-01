using Api.Constants;

namespace Api.Helpers;

/// <summary>
/// Standard error response helpers. Every one emits the single canonical body shape
/// (<see cref="OrkyoProblemDetails"/> — RFC 7807 plus a machine-readable <c>code</c>); this class
/// stays as the vocabulary of common failures so callers never hand-roll a status/code pair.
/// </summary>
public static class ErrorResponses
{
    /// <summary>
    /// 401 Unauthorized. The frontend switches on the code: <c>session_expired</c> clears state
    /// and redirects to login.
    /// </summary>
    public static IResult Unauthorized(string code = ApiErrorCodes.SessionExpired, string? message = null)
        => ProblemResults.Problem(StatusCodes.Status401Unauthorized, code, message ?? "Not authenticated");

    /// <summary>
    /// 403 Forbidden. A plain <c>forbidden</c> shows a toast; <c>break_glass_expired</c> exits the
    /// tenant to <paramref name="returnTo"/>.
    /// </summary>
    public static IResult Forbidden(string code = ApiErrorCodes.Forbidden, string? message = null, string? returnTo = null)
        => ProblemResults.Problem(StatusCodes.Status403Forbidden, code, message ?? "Forbidden", returnTo: returnTo);

    /// <summary>404 for a resource/id miss.</summary>
    /// <param name="resource">The resource type (e.g., "Request", "Space").</param>
    /// <param name="id">The ID that was not found.</param>
    public static IResult NotFound(string resource, Guid? id = null)
        => ProblemResults.Problem(
            StatusCodes.Status404NotFound, ErrorCodes.NotFound,
            id.HasValue ? $"{resource} with ID {id} not found" : $"{resource} not found",
            resourceType: resource);

    /// <summary>400 Bad Request.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="code">Optional error code (defaults to <see cref="ErrorCodes.ValidationError"/>).</param>
    public static IResult BadRequest(string message, string code = ErrorCodes.ValidationError)
        => ProblemResults.Problem(StatusCodes.Status400BadRequest, code, message);

    /// <summary>
    /// 404 with custom wording, for when the miss is not a simple resource/id lookup —
    /// e.g. "Unknown setting key: 'x'".
    /// </summary>
    public static IResult NotFoundMessage(string message)
        => ProblemResults.Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message);

    /// <summary>409 Conflict.</summary>
    public static IResult Conflict(string message)
        => ProblemResults.Problem(StatusCodes.Status409Conflict, ErrorCodes.Conflict, message);

    /// <summary>
    /// 422 Unprocessable Entity — well-formed but unactionable (e.g. a stale identity link),
    /// as distinct from a 400 validation failure.
    /// </summary>
    public static IResult UnprocessableEntity(string message)
        => ProblemResults.Problem(StatusCodes.Status422UnprocessableEntity, ErrorCodes.UnprocessableEntity, message);

    /// <summary>
    /// 403 for a quota-limit hit. The frontend renders "You've reached your X limit" from
    /// the resource type and limit.
    /// </summary>
    public static IResult QuotaExceeded(string resourceType, long limit, string message)
        => ProblemResults.Problem(
            StatusCodes.Status403Forbidden, ApiErrorCodes.QuotaExceeded, message,
            resourceType: resourceType, limit: limit);
}
