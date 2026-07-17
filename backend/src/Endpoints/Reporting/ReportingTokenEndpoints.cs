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

/// <summary>
/// Token management for the external reporting API. Error bodies here use the
/// <c>{ error, message }</c> shape — the DELIBERATE, stable contract of the external
/// reporting API surface, distinct from the internal <see cref="Api.Helpers.ErrorResponses"/>
/// shape. Do not converge; external consumers depend on it.
/// </summary>
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
        CancellationToken ct)
    {
        var tokens = await tokenService.ListForTenantAsync(tenant.TenantId, ct);
        return Results.Ok(tokens);
    }

    private static async Task<IResult> CreateToken(
        CreateReportingTokenRequest request,
        IReportingTokenService tokenService,
        ICurrentTenant tenant,
        ICurrentPrincipal principal,
        IFeatureGate featureGate,
        CancellationToken ct)
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
    }

    private static async Task<IResult> RevokeToken(
        Guid id,
        IReportingTokenService tokenService,
        ICurrentTenant tenant,
        ICurrentPrincipal principal,
        CancellationToken ct)
    {
        var revoked = await tokenService.RevokeAsync(
            id, tenant.TenantId,
            principal.UserId == Guid.Empty ? null : principal.UserId,
            ct);

        return revoked
            ? Results.NoContent()
            : ErrorResponses.NotFound("ReportingToken", id);
    }
}

public record CreateReportingTokenRequest(
    string Name,
    DateTime? ExpiresAt
);
