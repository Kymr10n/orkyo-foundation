using Api.Constants;

namespace Api.Helpers;

/// <summary>
/// The single error body shape for the Orkyo application API: RFC 7807 ProblemDetails plus the
/// machine-readable extensions the frontend switches on.
///
/// <para>Consolidated from five coexisting shapes (#96) — <c>ErrorResponse</c>'s
/// <c>{error, code}</c>, bare <c>Results.BadRequest</c> bodies, the CSRF middleware's
/// <c>{error}</c>, <c>ProblemDetailsHelper</c>'s auth problems, and framework
/// <c>ValidationProblem</c>. ValidationProblem was already RFC 7807, so consolidating means the
/// other four move to match the one that was already right, and <c>errors</c> (from validation)
/// slots in as just another extension.</para>
///
/// <para><b>Not</b> used by <c>/api/reporting/v1</c>: that surface is a versioned contract for
/// external BI consumers, and changing its error bodies would break them without a deprecation
/// window. See ReportingTokenAuthHandler.</para>
/// </summary>
public sealed record OrkyoProblemDetails
{
    /// <summary>URI reference identifying the problem type, derived from <see cref="Code"/>.</summary>
    public string? Type { get; init; }

    /// <summary>Short, stable human-readable summary.</summary>
    public required string Title { get; init; }

    /// <summary>Human-readable explanation of this specific occurrence.</summary>
    public string? Detail { get; init; }

    /// <summary>HTTP status code, repeated in the body per RFC 7807.</summary>
    public int Status { get; init; }

    /// <summary>Machine-readable error code the frontend switches on.</summary>
    public required string Code { get; init; }

    /// <summary>Resource type the error refers to, e.g. "spaces" on a quota_exceeded response.</summary>
    public string? ResourceType { get; init; }

    /// <summary>Relative path the frontend should navigate to after handling (e.g. "/site-admin").</summary>
    public string? ReturnTo { get; init; }

    /// <summary>Numeric limit associated with the error, e.g. the tier max on quota_exceeded.</summary>
    public long? Limit { get; init; }

    /// <summary>Per-field validation messages, present only on validation failures.</summary>
    public IDictionary<string, string[]>? Errors { get; init; }

    public static string TypeFor(string code) => $"https://orkyo.app/problems/{code}";
}

public static class ProblemResults
{
    /// <summary>
    /// Builds the canonical problem response. <paramref name="title"/> defaults to the status
    /// reason phrase so every caller does not have to invent one.
    /// </summary>
    public static IResult Problem(
        int status,
        string code,
        string? detail = null,
        string? title = null,
        string? resourceType = null,
        string? returnTo = null,
        long? limit = null,
        IDictionary<string, string[]>? errors = null)
        => Results.Json(
            new OrkyoProblemDetails
            {
                Type = OrkyoProblemDetails.TypeFor(code),
                Title = title ?? DefaultTitle(status),
                Detail = detail,
                Status = status,
                Code = code,
                ResourceType = resourceType,
                ReturnTo = returnTo,
                Limit = limit,
                Errors = errors,
            },
            statusCode: status,
            contentType: "application/problem+json");

    private static string DefaultTitle(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        410 => "Gone",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        _ => "Error",
    };
}
