using Api.Configuration;
using Api.Constants;
using Api.Helpers;
using Api.Middleware;
using Api.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

/// <summary>
/// The kinds of browser report the frontend sends (<c>frontend/src/lib/core/client-errors.ts</c>).
/// </summary>
public static class ClientReportKinds
{
    /// <summary>An uncaught exception reached <c>window.onerror</c>.</summary>
    public const string Error = "error";
    /// <summary>A promise rejected with nobody listening.</summary>
    public const string UnhandledRejection = "unhandledrejection";
    /// <summary>A React render threw and a <c>RouteErrorBoundary</c> caught it.</summary>
    public const string Render = "render";
    /// <summary>A web vital (LCP, CLS, TTFB, INP) sampled from a real session.</summary>
    public const string Vital = "vital";

    public static readonly IReadOnlyList<string> All = [Error, UnhandledRejection, Render, Vital];
    public static readonly IReadOnlyList<string> VitalNames = ["LCP", "CLS", "TTFB", "FID", "INP"];
}

/// <summary>One browser report. Errors carry a message and stack; vitals carry a name and value.</summary>
public sealed record ClientReportRequest(
    string Kind,
    string? Message,
    string? Stack,
    string? ComponentStack,
    /// <summary>Route path only (no query, no host): the frontend strips it before sending.</summary>
    string? Route,
    /// <summary>The <c>@kymr10n/foundation</c> package version the bundle was built from.</summary>
    string? Release,
    string? VitalName,
    double? VitalValue);

/// <summary>
/// Marker for the typed logger category so every client report lands under one stable
/// <c>SourceContext</c> (<c>Api.Endpoints.ClientReportLog</c>) that a Loki query can select.
/// </summary>
public sealed class ClientReportLog
{
}

public static class ClientErrorEndpoints
{
    public static void MapClientErrorEndpoints(this WebApplication app)
    {
        // POST /api/client-errors — the browser's one way to tell the server that something
        // broke in the browser (saas#303). Server-side observability is thorough; without this
        // a deploy that only fails in the browser is invisible to Loki and Prometheus.
        //
        // Anonymous: the login page and the apex can fail before any session exists, and a
        // report must not depend on the very thing that may be broken. CSRF-exempt: the
        // frontend sends it with `navigator.sendBeacon`, which cannot set the double-submit
        // header, and the endpoint changes no state of the caller's session. Per-IP rate limit:
        // a page in a render loop sends one report per second otherwise. Nothing is stored:
        // the report becomes one structured log event with the request's correlation id.
        app.MapPost("/api/client-errors", (
            [FromBody] ClientReportRequest request,
            IValidator<ClientReportRequest> validator,
            HttpContext ctx,
            ICurrentPrincipal principal,
            ILogger<ClientReportLog> logger) =>
        {
            var validation = validator.Validate(request);
            if (!validation.IsValid)
                return ErrorResponses.BadRequest(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));

            var userAgent = ctx.Request.Headers.UserAgent.ToString();
            var userId = principal.IsAuthenticated ? principal.UserIdOrNull : null;

            if (request.Kind == ClientReportKinds.Vital)
            {
                logger.LogInformation(
                    "Web vital {VitalName}={VitalValue} on {Route} (release {Release}, user {UserId}, agent {UserAgent})",
                    request.VitalName, request.VitalValue, request.Route, request.Release, userId, userAgent);
            }
            else
            {
                logger.LogError(
                    "Client error {Kind} on {Route}: {Message} (release {Release}, user {UserId}, agent {UserAgent}){Newline}{Stack}{Newline}{ComponentStack}",
                    request.Kind, request.Route, request.Message, request.Release, userId, userAgent,
                    Environment.NewLine, request.Stack, Environment.NewLine, request.ComponentStack);
            }

            return Results.Accepted();
        })
        .AllowAnonymous()
        .WithMetadata(new SkipTenantResolutionAttribute(), new CsrfExemptAttribute())
        .RequireRateLimiting(FoundationRateLimitPolicies.ClientErrors)
        .WithName("ReportClientError")
        .WithSummary("Record a browser-side error or web vital as a structured server log event")
        .WithTags("Observability");
    }
}
