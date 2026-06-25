using System.Text.Json;
using Api.Helpers;
using Api.Middleware;
using Api.Models.Export;
using Api.Security;
using Api.Security.Features;
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
            .RequireAdminArea()
            .WithTags("Export");

        group.MapPost("/", async ([FromBody] ExportRequest request, IFeatureGate featureGate, IExportService exportService, CancellationToken ct) =>
        {
            await featureGate.EnsureEnabledAsync(FeatureKeys.DataExport, ct);
            var payload = await exportService.ExportAsync(request, ct);
            return Results.Json(payload, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        })
        .WithName("ExportTenantData")
        .WithDescription("Exports tenant data as a canonical JSON payload")
        .Produces<ExportPayload>(200)
        .Produces(403);
    }
}
