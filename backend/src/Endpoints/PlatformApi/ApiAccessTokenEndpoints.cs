using Api.Helpers;
using Api.Middleware;
using Api.Security;
using Api.Security.Features;
using Api.Services;
using Api.Services.PlatformApi;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.PlatformApi;

/// <summary>
/// Management of API access tokens — the write-capable credentials an MCP client authenticates
/// with. Admin-only, like the reporting token surface: issuing one hands an automated caller the
/// ability to change the schedule, which is a governance decision rather than an editing one.
/// </summary>
public static class ApiAccessTokenEndpoints
{
    public static void MapApiAccessTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/v1/tokens")
            .RequireAuthorization()
            .RequireAdminArea()
            .WithTags("API Access Tokens");

        group.MapGet("/", ListTokens)
            .WithName("ListApiAccessTokens")
            .WithSummary("List API access tokens for the authenticated tenant");

        group.MapPost("/", CreateToken)
            .WithName("CreateApiAccessToken")
            .WithSummary("Create a new API access token (secret shown once only)");

        group.MapDelete("/{id:guid}", RevokeToken)
            .WithName("RevokeApiAccessToken")
            .WithSummary("Revoke an API access token");
    }

    private static async Task<IResult> ListTokens(
        IApiAccessTokenService tokenService,
        ICurrentTenant tenant,
        CancellationToken ct)
    {
        return Results.Ok(await tokenService.ListForTenantAsync(tenant.TenantId, ct));
    }

    private static async Task<IResult> CreateToken(
        CreateApiAccessTokenRequest request,
        IValidator<CreateApiAccessTokenRequest> validator,
        IApiAccessTokenService tokenService,
        ICurrentTenant tenant,
        ICurrentPrincipal principal,
        IFeatureGate featureGate,
        CancellationToken ct)
    {
        // Same entitlement as the reporting API: programmatic access is one product capability,
        // not two. The edition decides — SaaS grants it on paid tiers, Community allows everything.
        if (!await featureGate.IsEnabledAsync(FeatureKeys.ApiAccess, ct))
            return ErrorResponses.UpgradeRequired("API access requires a paid plan.");

        return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
        {
            var created = await tokenService.CreateAsync(
                tenant.TenantId,
                request.Name.Trim(),
                request.Scopes,
                request.ExpiresAt,
                principal.UserIdOrNull,
                ct);
            return Results.Created($"/api/platform/v1/tokens/{created.Summary.Id}", created);
        }, ct);
    }

    private static async Task<IResult> RevokeToken(
        Guid id,
        IApiAccessTokenService tokenService,
        ICurrentTenant tenant,
        ICurrentPrincipal principal,
        CancellationToken ct)
    {
        var revoked = await tokenService.RevokeAsync(id, tenant.TenantId, principal.UserIdOrNull, ct);
        return EndpointHelpers.NoContentOrNotFound(revoked, "ApiAccessToken", id);
    }
}

public record CreateApiAccessTokenRequest(
    string Name,
    IReadOnlyList<string> Scopes,
    DateTime? ExpiresAt
);
