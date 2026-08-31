using Api.Configuration;
using Api.Helpers;
using Api.Middleware;
using Api.PlatformApi.Auth;
using Api.Security;
using Api.Services.PlatformApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Endpoints.PlatformApi;

/// <summary>
/// The MCP server: one Streamable HTTP endpoint an MCP-compatible client (an LLM agent, or any
/// automated service) connects to with an <c>orkyo_api_*</c> token to read and change the tenant's
/// schedule.
///
/// Authorization deliberately splits across two levels. The group requires a valid API token whose
/// tenant matches the resolved host, and tenant membership — which for a token means its scopes
/// mapped to a real <see cref="TenantRole"/> by ContextEnrichmentMiddleware. The per-tool Editor
/// check lives in <see cref="Api.PlatformApi.Mcp.ScheduleTools"/> rather than in the group's
/// verb-aware write gate, because MCP carries every call over one POST: gating on the HTTP verb
/// would require Editor merely to list the tools. The threshold itself is not duplicated — the
/// tools test <see cref="IAuthorizationContext.CanEdit"/>, the same Role &gt;= Editor property the
/// HTTP write gate uses.
/// </summary>
public static class McpEndpoints
{
    public static void MapOrkyoMcpEndpoints(this IEndpointRouteBuilder app)
    {
        var mcp = app.MapMcp("/api/mcp")
            .RequireAuthorization(ApiAccessTokenAuthHandler.PolicyName)
            .RequireRateLimiting(FoundationRateLimitPolicies.McpApi)
            // The token's scopes become a tenant role, so this is the same membership gate every
            // tenant-scoped HTTP group applies.
            .RequireTenantMembership()
            // Stamped explicitly: this route is a POST, and the conformance test requires every
            // mutating /api route to declare a convention. The convention here is the per-tool
            // Editor check described above, not the verb-aware gate.
            .WithMetadata(new AuthorizationGoverned())
            .WithTags("MCP");

        // Defence in depth against a token replayed against another tenant's host. The middleware
        // already refuses to grant a role in that case; this turns it into an explicit refusal
        // instead of a bare membership denial.
        mcp.AddEndpointFilter(async (ctx, next) =>
        {
            var record = ctx.HttpContext.Items[ApiAccessTokenContextKeys.TokenRecord]
                as ApiAccessTokenRecord;
            var currentTenant = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();

            if (record is null || !currentTenant.HasTenant)
                return ErrorResponses.Unauthorized("invalid_api_token");

            if (record.TenantId != currentTenant.TenantId)
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<EndpointLoggerCategory>>();
                logger.LogWarning(
                    "API token tenant mismatch: token={TokenTenant}, request={RequestTenant}",
                    record.TenantId, currentTenant.TenantId);
                return ErrorResponses.Forbidden();
            }

            return await next(ctx);
        });
    }
}
