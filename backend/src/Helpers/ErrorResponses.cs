using Api.Constants;

namespace Api.Helpers;

/// <summary>
/// Standard error response helpers for consistent API error responses.
/// </summary>
public static class ErrorResponses
{
    /// <summary>
    /// Returns a 401 Unauthorized response with a structured body the frontend can switch on.
    /// </summary>
    public static IResult Unauthorized(string code = ApiErrorCodes.SessionExpired, string? message = null)
    {
        return Results.Json(new ErrorResponse
        {
            Error = message ?? "Not authenticated",
            Code = code,
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Returns a 403 Forbidden response with a structured body. The frontend switches behavior on the code:
    /// a plain <c>forbidden</c> shows a toast, <c>break_glass_expired</c> exits the tenant to <paramref name="returnTo"/>.
    /// </summary>
    public static IResult Forbidden(string code = ApiErrorCodes.Forbidden, string? message = null, string? returnTo = null)
    {
        return Results.Json(new ErrorResponse
        {
            Error = message ?? "Forbidden",
            Code = code,
            ReturnTo = returnTo,
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Returns a 404 Not Found response with a standard error format.
    /// </summary>
    /// <param name="resource">The resource type (e.g., "Request", "Space").</param>
    /// <param name="id">The ID that was not found.</param>
    public static IResult NotFound(string resource, Guid? id = null)
    {
        var message = id.HasValue
            ? $"{resource} with ID {id} not found"
            : $"{resource} not found";

        return Results.NotFound(new ErrorResponse
        {
            Error = message,
            Code = ErrorCodes.NotFound,
            ResourceType = resource
        });
    }

    /// <summary>
    /// Returns a 400 Bad Request response with a standard error format.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="code">Optional error code (defaults to VALIDATION_ERROR).</param>
    public static IResult BadRequest(string message, string code = nameof(ErrorCodes.ValidationError))
    {
        return Results.BadRequest(new ErrorResponse
        {
            Error = message,
            Code = code
        });
    }

    /// <summary>
    /// Returns a 409 Conflict response with a standard error format.
    /// </summary>
    /// <param name="message">The error message.</param>
    public static IResult Conflict(string message)
    {
        return Results.Conflict(new ErrorResponse
        {
            Error = message,
            Code = ErrorCodes.Conflict
        });
    }
}

/// <summary>
/// Standard error response format.
/// </summary>
public record ErrorResponse
{
    public required string Error { get; init; }
    public required string Code { get; init; }
    public string? ResourceType { get; init; }
    /// <summary>Optional URL the frontend should navigate to after handling the error (e.g. "/admin" when a break-glass session ends).</summary>
    public string? ReturnTo { get; init; }
}