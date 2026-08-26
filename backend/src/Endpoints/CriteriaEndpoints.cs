using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class CriteriaEndpoints
{
    public static void MapCriteriaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/criteria").WithTags("Criteria").RequireAuthorization().RequireMemberReadEditorWrite();

        group.MapGet("/", async (ICriteriaService criteriaService, IResourceTypeRepository resourceTypeRepository, int? page, int? pageSize, string? resourceType, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(resourceType))
            {
                // Tenants define their own resource types, so the set of valid keys lives in the DB.
                if (await resourceTypeRepository.GetByKeyAsync(resourceType, ct) is null)
                    return ErrorResponses.BadRequest($"Unknown resource type '{resourceType}'.");
                return Results.Ok(await criteriaService.GetByResourceTypeAsync(resourceType, ct));
            }
            if (page.HasValue || pageSize.HasValue)
            {
                var paged = await criteriaService.GetAllAsync(new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize }, ct);
                return Results.Ok(paged);
            }
            return Results.Ok(await criteriaService.GetAllAsync(ct));
        })
        .WithName("GetCriteria")
        .WithSummary("Get all criteria");

        group.MapGet("/{id:guid}", async (Guid id, ICriteriaService criteriaService, CancellationToken ct) =>
        {
            var criterion = await criteriaService.GetByIdAsync(id, ct);
            return EndpointHelpers.OkOrNotFound(criterion, "Criterion", id);
        })
        .WithName("GetCriterionById")
        .WithSummary("Get a specific criterion by ID");

        group.MapPost("/", async (CreateCriterionRequest request, ICriteriaService criteriaService, ILogger<EndpointLoggerCategory> logger, IValidator<CreateCriterionRequest> validator, CancellationToken ct) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var criterion = await criteriaService.CreateAsync(
                    request.Name,
                    request.Description,
                    request.DataType,
                    request.EnumValues,
                    request.Unit,
                    request.Validation,
                    request.ResourceTypeKeys!, ct);
                return Results.Created($"/criteria/{criterion.Id}", criterion);
            }, logger, "create criterion");
        })
        .WithName("CreateCriterion")
        .WithSummary("Create a new criterion");

        group.MapPut("/{id:guid}", async (Guid id, UpdateCriterionRequest request, ICriteriaService criteriaService, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateCriterionRequest> validator, CancellationToken ct) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var criterion = await criteriaService.UpdateAsync(id, request.Name, request.Description, request.EnumValues, request.Unit, request.Validation, request.DataType, ct);
                return EndpointHelpers.OkOrNotFound(criterion, "Criterion", id);
            }, logger, "update criterion", new { id });
        })
        .WithName("UpdateCriterion")
        .WithSummary("Update an existing criterion");

        group.MapDelete("/{id:guid}", async (Guid id, ICriteriaService criteriaService, CancellationToken ct) =>
        {
            var deleted = await criteriaService.DeleteAsync(id, ct);
            return EndpointHelpers.NoContentOrNotFound(deleted, "Criterion", id);
        })
        .WithName("DeleteCriterion")
        .WithSummary("Delete a criterion");
    }
}
