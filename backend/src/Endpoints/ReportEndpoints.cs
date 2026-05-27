using Api.Middleware;
using Api.Reporting;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        // Tenant-scoped group — RequireTenantMembership() already applied by the
        // product's tenant group; these endpoints add role gating on top.
        var group = app.MapGroup("/api/reports")
            .RequireAuthorization()
            .WithTags("Reports");

        group.MapGet("", GetCatalogue)
            .RequireRole(TenantRole.Viewer, TenantRole.Editor, TenantRole.Admin)
            .WithName("GetReports")
            .WithSummary("List reports visible to the caller")
            .Produces<IReadOnlyList<ReportDefinition>>(StatusCodes.Status200OK);

        group.MapPost("{reportKey}/embed-token", CreateEmbedToken)
            .RequireRole(TenantRole.Viewer, TenantRole.Editor, TenantRole.Admin)
            .WithName("CreateReportEmbedToken")
            .WithSummary("Issue a short-lived Superset guest token for a report")
            .Produces<ReportEmbedTokenResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        // Admin reprovision — triggers idempotent re-provisioning for the current tenant.
        app.MapPost("/api/admin/reports/reprovision", Reprovision)
            .RequireAuthorization()
            .RequireRole(TenantRole.Admin)
            .WithTags("Reports")
            .WithName("ReprovisionReporting")
            .WithSummary("Re-run reporting provisioning for the current tenant");
    }

    private static IResult GetCatalogue(IReportCatalogService catalog)
    {
        return Results.Ok(catalog.GetVisibleReports());
    }

    private static async Task<IResult> CreateEmbedToken(
        string reportKey,
        IReportEmbedService embed,
        CancellationToken ct)
    {
        var result = await embed.CreateEmbedTokenAsync(reportKey, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> Reprovision(
        ICurrentTenant tenant,
        ITenantReportingProvisioner provisioner,
        IDbConnectionFactory db,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();

        // Resolve db_identifier from the control-plane — more reliable than parsing
        // the connection string (which is an implementation detail of IDbConnectionFactory).
        await using var conn = db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT db_identifier FROM public.tenants WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        var dbIdentifier = await cmd.ExecuteScalarAsync(ct) as string
            ?? throw new Api.Helpers.NotFoundException("Tenant", tenantId);

        // Fire-and-forget: provisioning may take several seconds.
        _ = Task.Run(async () =>
        {
            try
            {
                await provisioner.ProvisionAsync(tenantId, dbIdentifier, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background reprovision failed for tenant {TenantId}", tenantId);
            }
        }, ct);

        return Results.Accepted(null, new { message = "Reporting provisioning started" });
    }
}
