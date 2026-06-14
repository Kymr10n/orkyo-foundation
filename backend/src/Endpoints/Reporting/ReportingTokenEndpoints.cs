using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Security.Features;
using Api.Services;
using Api.Services.Reporting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Reporting;

public static class ReportingTokenEndpoints
{
    public static void MapReportingTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/v1/tokens")
            .RequireAuthorization()
            .RequireAdminArea()
            .WithTags("Reporting API Tokens");

        group.MapGet("/", ListTokens)
            .WithName("ListReportingTokens")
            .WithSummary("List reporting tokens for the authenticated tenant");

        group.MapPost("/", CreateToken)
            .WithName("CreateReportingToken")
            .WithSummary("Create a new reporting token (secret shown once only)");

        group.MapDelete("/{id:guid}", RevokeToken)
            .WithName("RevokeReportingToken")
            .WithSummary("Revoke a reporting token");
    }

    private static async Task<IResult> ListTokens(
        IReportingTokenService tokenService,
        ICurrentTenant tenant,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct)
    {
        return await EndpointHelpers.ExecuteAsync(async () =>
        {
            var tokens = await tokenService.ListForTenantAsync(tenant.TenantId, ct);
            return Results.Ok(tokens);
        }, logger, "list reporting tokens");
    }

    private static async Task<IResult> CreateToken(
        CreateReportingTokenRequest request,
        IReportingTokenService tokenService,
        ICurrentTenant tenant,
        ICurrentPrincipal principal,
        IFeatureGate featureGate,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct)
    {
        return await EndpointHelpers.ExecuteAsync(async () =>
        {
            // Reporting API keys require the API-access entitlement. The edition decides:
            // SaaS grants it on paid tiers; Community allows everything.
            if (!await featureGate.IsEnabledAsync(FeatureKeys.ApiAccess, ct))
                return Results.Json(
                    new { error = "upgrade_required", message = "Reporting API access requires a paid plan." },
                    statusCode: StatusCodes.Status402PaymentRequired);

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "validation_failed", message = "Token name is required." });

            var created = await tokenService.CreateAsync(
                tenant.TenantId,
                request.Name.Trim(),
                request.ExpiresAt,
                principal.UserId == Guid.Empty ? null : principal.UserId,
                ct);

            return Results.Created($"/api/reporting/v1/tokens/{created.Summary.Id}", created);
        }, logger, "create reporting token", new { name = request.Name });
    }

    private static async Task<IResult> RevokeToken(
        Guid id,
        IReportingTokenService tokenService,
        ICurrentTenant tenant,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct)
    {
        return await EndpointHelpers.ExecuteAsync(async () =>
        {
            var revoked = await tokenService.RevokeAsync(
                id, tenant.TenantId,
                principal.UserId == Guid.Empty ? null : principal.UserId,
                ct);

            return revoked
                ? Results.NoContent()
                : ErrorResponses.NotFound("ReportingToken", id);
        }, logger, "revoke reporting token", new { id });
    }
}

public record CreateReportingTokenRequest(
    string Name,
    DateTime? ExpiresAt
);
