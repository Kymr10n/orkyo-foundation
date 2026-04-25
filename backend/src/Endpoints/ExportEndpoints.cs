using System.Text.Json;
using Api.Helpers;
using Api.Models.Export;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/export")
            .RequireAuthorization()
            .WithTags("Export");

        group.MapPost("/", async ([FromBody] ExportRequest request, IAuthorizationContext authContext, IExportService exportService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                authContext.RequireRole(TenantRole.Admin);
                var payload = await exportService.ExportAsync(request);
                return Results.Json(payload, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }, logger, "export tenant data", new { siteIds = request.SiteIds, masterData = request.IncludeMasterData, planningData = request.IncludePlanningData });
        })
        .WithName("ExportTenantData")
        .WithDescription("Exports tenant data as a canonical JSON payload")
        .Produces<ExportPayload>(200)
        .Produces(403);
    }
}
