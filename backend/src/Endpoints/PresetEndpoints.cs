using System.Text.Json;
using Api.Helpers;
using Api.Models.Preset;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class PresetEndpoints
{
    public static void MapPresetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/presets")
            .RequireAuthorization()
            .WithTags("Presets");

        group.MapPost("/validate", async ([FromBody] Preset preset, IPresetService presetService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var result = await presetService.ValidateAsync(preset);
                return Results.Ok(result);
            }, logger, "validate preset", new { presetId = preset.PresetId });
        })
        .WithName("ValidatePreset")
        .WithDescription("Validates a preset JSON without applying it")
        .Produces<PresetValidationResult>(200);

        group.MapPost("/apply", async ([FromBody] Preset preset, ICurrentPrincipal principal, IPresetService presetService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var userId = principal.RequireUserId();
                var result = await presetService.ApplyAsync(preset, userId);
                return result.Success ? Results.Ok(result) : Results.BadRequest(new { error = result.Error });
            }, logger, "apply preset", new { presetId = preset.PresetId });
        })
        .WithName("ApplyPreset")
        .WithDescription("Applies a preset to the current tenant")
        .Produces<PresetApplicationResult>(200);

        group.MapGet("/export", async ([FromQuery] string presetId, [FromQuery] string name, [FromQuery] string? description, IPresetService presetService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(presetId) || string.IsNullOrWhiteSpace(name))
                    return Results.BadRequest(new { error = "presetId and name are required" });
                var preset = await presetService.ExportAsync(presetId, name, description);
                return Results.Json(preset, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }, logger, "export preset", new { presetId, name });
        })
        .WithName("ExportPreset")
        .WithDescription("Exports the current tenant configuration as a preset JSON");

        group.MapGet("/applications", async (IPresetService presetService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await presetService.GetApplicationsAsync()),
            logger, "get preset applications");
        })
        .WithName("GetPresetApplications")
        .WithDescription("Gets the history of presets applied to this tenant");
    }
}
