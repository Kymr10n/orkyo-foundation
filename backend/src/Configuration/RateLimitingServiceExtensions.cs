using System.Threading.RateLimiting;
using Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Configuration;

/// <summary>
/// Names of the rate-limiting policies required by foundation's shared endpoints.
/// Both the registration (<see cref="RateLimitingServiceExtensions.AddFoundationRateLimiting"/>)
/// and the endpoint call-sites reference these constants, so a foundation-owned policy name
/// has a single source of truth and cannot drift between the two.
/// </summary>
public static class FoundationRateLimitPolicies
{
    public const string AdminOperations = "admin-operations";
    public const string PasswordChange = "password-change";
    public const string SessionBootstrap = "session-bootstrap";
    public const string ContactForm = "contact-form";
    public const string BffAuth = "bff-auth";
    public const string ReportingApi = "reporting-api";
}

/// <summary>
/// Registers exactly the rate-limit policies that foundation's own endpoints require via
/// <c>RequireRateLimiting(FoundationRateLimitPolicies.X)</c>. Each edition calls this so that
/// mapping a foundation endpoint can never 500 at request time on a missing policy. Editions add
/// their own extra policies on top (e.g. SaaS demo-login / break-glass) in a separate
/// <c>AddRateLimiter</c> call — policy names are disjoint, so the registrations accumulate.
/// </summary>
public static class RateLimitingServiceExtensions
{
    public static IServiceCollection AddFoundationRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Admin surface (/api/admin/*) — per-user ceiling on admin operations.
            options.AddPolicy(FoundationRateLimitPolicies.AdminOperations, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    UserOrIpKey(ctx),
                    _ => Window(permitLimit: 30, TimeSpan.FromMinutes(1))));

            // SecurityEndpoints — per-user ceiling on password changes.
            options.AddPolicy(FoundationRateLimitPolicies.PasswordChange, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    UserOrIpKey(ctx),
                    _ => Window(permitLimit: 5, TimeSpan.FromMinutes(15))));

            // Reporting API — per reporting-token id (embedded in the identity name after auth).
            options.AddPolicy(FoundationRateLimitPolicies.ReportingApi, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    UserOrIpKey(ctx),
                    _ => Window(permitLimit: 60, TimeSpan.FromMinutes(1))));

            // SessionEndpoints — per-IP ceiling on session bootstrap.
            options.AddPolicy(FoundationRateLimitPolicies.SessionBootstrap, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    IpKey(ctx),
                    _ => Window(permitLimit: 10, TimeSpan.FromMinutes(1))));

            // ContactEndpoints — per-IP ceiling on the public contact form.
            options.AddPolicy(FoundationRateLimitPolicies.ContactForm, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    IpKey(ctx),
                    _ => Window(permitLimit: 3, TimeSpan.FromHours(1))));

            // BffAuthEndpoints — per-IP ceiling on the anonymous login/callback/logout endpoints.
            options.AddPolicy(FoundationRateLimitPolicies.BffAuth, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    IpKey(ctx),
                    _ => Window(permitLimit: 20, TimeSpan.FromMinutes(1))));
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permitLimit, TimeSpan window) => new()
    {
        PermitLimit = permitLimit,
        Window = window,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0,
    };

    /// <summary>Partition by authenticated identity, falling back to the client IP.</summary>
    private static string UserOrIpKey(HttpContext ctx) => ctx.User?.Identity?.Name ?? IpKey(ctx);

    /// <summary>Partition by the real client IP (Cloudflare → nginx aware), never null.</summary>
    private static string IpKey(HttpContext ctx) =>
        ctx.RequestServices.GetRequiredService<IClientIpAccessor>().GetClientIp(ctx) ?? "unknown";
}
