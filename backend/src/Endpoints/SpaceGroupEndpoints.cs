using Api.Middleware;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class SpaceGroupEndpoints
{
    public static void MapSpaceGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/groups")
            .WithTags("Space Groups")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/", async (ISpaceGroupRepository repo, ILogger<EndpointLoggerCategory> logger, int? page, int? pageSize) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (page.HasValue || pageSize.HasValue)
                {
                    var paged = await repo.GetAllAsync(new PageRequest { Page = page ?? 1, PageSize = pageSize ?? PageRequest.DefaultPageSize });
                    return Results.Ok(paged);
                }
                return Results.Ok(await repo.GetAllAsync());
            }, logger, "list space groups");
        })
        .WithName("GetSpaceGroups")
        .WithSummary("Get all space groups");

        group.MapGet("/{id:guid}", async (Guid id, ISpaceGroupRepository repo, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var spaceGroup = await repo.GetByIdAsync(id);
                return spaceGroup == null ? ErrorResponses.NotFound("Space group", id) : Results.Ok(spaceGroup);
            }, logger, "get space group", new { id });
        })
        .WithName("GetSpaceGroup")
        .WithSummary("Get a space group by ID");

        group.MapPost("/", async (CreateSpaceGroupRequest request, ISpaceGroupRepository repo, ILogger<EndpointLoggerCategory> logger, IValidator<CreateSpaceGroupRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var spaceGroup = await repo.CreateAsync(request.Name, request.Description, request.Color, request.DisplayOrder);
                return Results.Created($"/api/groups/{spaceGroup.Id}", spaceGroup);
            }, logger, "create space group", new { name = request.Name });
        })
        .WithName("CreateSpaceGroup")
        .WithSummary("Create a new space group");

        group.MapPut("/{id:guid}", async (Guid id, UpdateSpaceGroupRequest request, ISpaceGroupRepository repo, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateSpaceGroupRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var spaceGroup = await repo.UpdateAsync(id, request.Name, request.Description, request.Color, request.DisplayOrder);
                return spaceGroup == null ? ErrorResponses.NotFound("Space group", id) : Results.Ok(spaceGroup);
            }, logger, "update space group", new { id });
        })
        .WithName("UpdateSpaceGroup")
        .WithSummary("Update a space group");

        group.MapDelete("/{id:guid}", async (Guid id, ISpaceGroupRepository repo, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await repo.DeleteAsync(id);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Space group", id);
            }, logger, "delete space group", new { id });
        })
        .WithName("DeleteSpaceGroup")
        .WithSummary("Delete a space group (spaces become ungrouped)");
    }
}
