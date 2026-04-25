using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/templates").RequireAuthorization();

        group.MapGet("", async (ITemplateRepository templateRepo, ILogger<EndpointLoggerCategory> logger, string? entityType) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (string.IsNullOrEmpty(entityType)) return Results.BadRequest(new { error = "entityType query parameter is required" });
                if (!new[] { "request", "space", "group" }.Contains(entityType)) return Results.BadRequest(new { error = $"Invalid entity type: {entityType}" });
                return Results.Ok(await templateRepo.GetAllAsync(entityType));
            }, logger, "get templates", new { entityType });
        });

        group.MapGet("{id:guid}", async (ITemplateRepository templateRepo, ILogger<EndpointLoggerCategory> logger, Guid id) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var template = await templateRepo.GetByIdAsync(id);
                return template == null ? ErrorResponses.NotFound("Template", id) : Results.Ok(template);
            }, logger, "get template by id", new { id });
        });

        group.MapPost("", async (ITemplateRepository templateRepo, ILogger<EndpointLoggerCategory> logger, CreateTemplateRequest request) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var template = await templateRepo.CreateAsync(request);
                return Results.Created($"/api/templates/{template.Id}", template);
            }, logger, "create template", new { name = request.Name, entityType = request.EntityType });
        });

        group.MapDelete("{id:guid}", async (ITemplateRepository templateRepo, ILogger<EndpointLoggerCategory> logger, Guid id) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await templateRepo.DeleteAsync(id);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Template", id);
            }, logger, "delete template", new { id });
        });

        group.MapPut("{id:guid}", async (ITemplateRepository templateRepo, ILogger<EndpointLoggerCategory> logger, Guid id, UpdateTemplateRequest request) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var template = await templateRepo.UpdateAsync(id, request);
                return template == null ? ErrorResponses.NotFound("Template", id) : Results.Ok(template);
            }, logger, "update template", new { id, name = request.Name });
        });

        group.MapGet("{id:guid}/items", async (ITemplateRepository templateRepo, ILogger<EndpointLoggerCategory> logger, Guid id) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var template = await templateRepo.GetByIdAsync(id);
                if (template == null) return ErrorResponses.NotFound("Template", id);
                return Results.Ok(await templateRepo.GetTemplateItemsAsync(id));
            }, logger, "get template items", new { id });
        });

        group.MapPost("{id:guid}/items", async (ITemplateRepository templateRepo, ILogger<EndpointLoggerCategory> logger, Guid id, CreateTemplateItemRequest request) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var item = new TemplateItem { TemplateId = id, CriterionId = request.CriterionId, Value = request.Value };
                var created = await templateRepo.CreateTemplateItemAsync(item);
                return Results.Created($"/api/templates/{id}/items/{created.Id}", created);
            }, logger, "add template item", new { templateId = id, criterionId = request.CriterionId });
        });

        group.MapDelete("{templateId:guid}/items/{itemId:guid}", async (ITemplateRepository templateRepo, ILogger<EndpointLoggerCategory> logger, Guid templateId, Guid itemId) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await templateRepo.DeleteTemplateItemAsync(itemId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Template item", itemId);
            }, logger, "delete template item", new { templateId, itemId });
        });
    }
}
