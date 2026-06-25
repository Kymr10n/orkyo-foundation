using Api.Constants;
using Api.Helpers;
using Api.Middleware;
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
        var group = app.MapGroup("/api/templates").RequireAuthorization().RequireMemberReadEditorWrite();

        group.MapGet("", async (ITemplateRepository templateRepo, string? entityType) =>
        {
            if (string.IsNullOrEmpty(entityType)) return ErrorResponses.BadRequest("entityType query parameter is required");
            if (!TemplateEntityTypes.IsKnown(entityType)) return ErrorResponses.BadRequest($"Invalid entity type: {entityType}");
            return Results.Ok(await templateRepo.GetAllAsync(entityType));
        });

        group.MapGet("{id:guid}", async (ITemplateRepository templateRepo, Guid id) =>
        {
            var template = await templateRepo.GetByIdAsync(id);
            return EndpointHelpers.OkOrNotFound(template, "Template", id);
        });

        group.MapPost("", async (ITemplateRepository templateRepo, CreateTemplateRequest request) =>
        {
            var template = await templateRepo.CreateAsync(request);
            return Results.Created($"/api/templates/{template.Id}", template);
        });

        group.MapDelete("{id:guid}", async (ITemplateRepository templateRepo, Guid id) =>
        {
            var deleted = await templateRepo.DeleteAsync(id);
            return deleted ? Results.NoContent() : ErrorResponses.NotFound("Template", id);
        });

        group.MapPut("{id:guid}", async (ITemplateRepository templateRepo, Guid id, UpdateTemplateRequest request) =>
        {
            var template = await templateRepo.UpdateAsync(id, request);
            return EndpointHelpers.OkOrNotFound(template, "Template", id);
        });

        group.MapGet("{id:guid}/items", async (ITemplateRepository templateRepo, Guid id) =>
        {
            var template = await templateRepo.GetByIdAsync(id);
            if (template == null) return ErrorResponses.NotFound("Template", id);
            return Results.Ok(await templateRepo.GetTemplateItemsAsync(id));
        });

        group.MapPost("{id:guid}/items", async (ITemplateRepository templateRepo, Guid id, CreateTemplateItemRequest request) =>
        {
            var item = new TemplateItem { TemplateId = id, CriterionId = request.CriterionId, Value = request.Value };
            var created = await templateRepo.CreateTemplateItemAsync(item);
            return Results.Created($"/api/templates/{id}/items/{created.Id}", created);
        });

        group.MapDelete("{templateId:guid}/items/{itemId:guid}", async (ITemplateRepository templateRepo, Guid templateId, Guid itemId) =>
        {
            var deleted = await templateRepo.DeleteTemplateItemAsync(itemId);
            return deleted ? Results.NoContent() : ErrorResponses.NotFound("Template item", itemId);
        });
    }
}
