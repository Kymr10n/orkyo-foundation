using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class RoutingEndpoints
{
    public static void MapRoutingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/routings")
            .WithTags("Routings")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        group.MapGet("", async (IRoutingService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)));

        group.MapGet("{id:guid}", async (IRoutingService service, Guid id, CancellationToken ct) =>
            EndpointHelpers.OkOrNotFound(await service.GetByIdAsync(id, ct), "Routing", id));

        group.MapPost("", async (IRoutingService service, CreateRoutingRequest request,
            IValidator<CreateRoutingRequest> validator, CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var routing = await service.CreateAsync(request, ct);
                return Results.Created($"/api/routings/{routing.Id}", routing);
            }));

        group.MapPut("{id:guid}", async (IRoutingService service, Guid id, UpdateRoutingRequest request,
            IValidator<UpdateRoutingRequest> validator, CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
                EndpointHelpers.OkOrNotFound(await service.UpdateAsync(id, request, ct), "Routing", id)));

        group.MapDelete("{id:guid}", async (IRoutingService service, Guid id, CancellationToken ct) =>
            EndpointHelpers.NoContentOrNotFound(await service.DeleteAsync(id, ct), "Routing", id));

        // Mutating: it writes a container, its leaves and their edges, so it stays behind the
        // group's editor gate.
        group.MapPost("{id:guid}/instantiate", async (IRoutingService service, Guid id,
            InstantiateRoutingRequest request, IValidator<InstantiateRoutingRequest> validator,
            CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var result = await service.InstantiateAsync(id, request, ct);
                return Results.Created($"/api/requests/{result.Parent.Id}", result);
            }))
        .WithSummary("Create a work order from a routing: one leaf per step, chained finish-to-start");
    }
}
