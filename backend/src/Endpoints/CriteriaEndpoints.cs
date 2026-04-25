using Api.Middleware;
using Api.Helpers;
using Api.Models;
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
        var group = app.MapGroup("/api/criteria").WithTags("Criteria").RequireAuthorization().RequireTenantMembership();

        group.MapGet("/", async (ICriteriaService criteriaService, ILogger<EndpointLoggerCategory> logger, int? page, int? pageSize) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (page.HasValue || pageSize.HasValue)
                {
                    var paged = await criteriaService.GetAllAsync(new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize });
                    return Results.Ok(paged);
                }
                return Results.Ok(await criteriaService.GetAllAsync());
            }, logger, "list criteria");
        })
        .WithName("GetCriteria")
        .WithSummary("Get all criteria");

        group.MapGet("/{id:guid}", async (Guid id, ICriteriaService criteriaService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var criterion = await criteriaService.GetByIdAsync(id);
                return criterion == null ? ErrorResponses.NotFound("Criterion", id) : Results.Ok(criterion);
            }, logger, "get criterion", new { id });
        })
        .WithName("GetCriterionById")
        .WithSummary("Get a specific criterion by ID");

        group.MapPost("/", async (CreateCriterionRequest request, ICriteriaService criteriaService, ILogger<EndpointLoggerCategory> logger, IValidator<CreateCriterionRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var criterion = await criteriaService.CreateAsync(request.Name, request.Description, request.DataType, request.EnumValues, request.Unit);
                return Results.Created($"/criteria/{criterion.Id}", criterion);
            }, logger, "create criterion");
        })
        .WithName("CreateCriterion")
        .WithSummary("Create a new criterion");

        group.MapPut("/{id:guid}", async (Guid id, UpdateCriterionRequest request, ICriteriaService criteriaService, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateCriterionRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var criterion = await criteriaService.UpdateAsync(id, request.Description, request.EnumValues, request.Unit);
                return criterion == null ? ErrorResponses.NotFound("Criterion", id) : Results.Ok(criterion);
            }, logger, "update criterion", new { id });
        })
        .WithName("UpdateCriterion")
        .WithSummary("Update an existing criterion");

        group.MapDelete("/{id:guid}", async (Guid id, ICriteriaService criteriaService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await criteriaService.DeleteAsync(id);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Criterion", id);
            }, logger, "delete criterion", new { id });
        })
        .WithName("DeleteCriterion")
        .WithSummary("Delete a criterion");
    }
}
