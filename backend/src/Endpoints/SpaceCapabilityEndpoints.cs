using Api.Middleware;
using Api.Helpers;
using Api.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class SpaceCapabilityEndpoints
{
    public static void MapSpaceCapabilityEndpoints(this WebApplication app)
    {
        var capabilities = app.MapGroup("/api/sites/{siteId:guid}/spaces/{spaceId:guid}/capabilities")
            .WithTags("Space Capabilities")
            .RequireAuthorization()
            .RequireTenantMembership();

        capabilities.MapGet("/", async (Guid siteId, Guid spaceId, ISpaceCapabilityRepository repository, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await repository.GetAllAsync(siteId, spaceId)),
            logger, "get space capabilities", new { siteId, spaceId });
        })
        .WithName("GetSpaceCapabilities")
        .WithSummary("Get all capabilities for a space");

        capabilities.MapPost("/", async (Guid siteId, Guid spaceId, AddSpaceCapabilityRequest request, ISpaceCapabilityRepository repository, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var capability = await repository.CreateAsync(siteId, spaceId, request.CriterionId, request.Value);
                logger.LogInformation("Added capability {CapabilityId} to space {SpaceId}", capability.Id, spaceId);
                return Results.Created($"/sites/{siteId}/spaces/{spaceId}/capabilities/{capability.Id}", capability);
            }, logger, "add space capability", new { siteId, spaceId });
        })
        .WithName("AddSpaceCapability")
        .WithSummary("Add a capability to a space");

        capabilities.MapPut("/{capabilityId:guid}", async (Guid siteId, Guid spaceId, Guid capabilityId, UpdateSpaceCapabilityRequest request, ISpaceCapabilityRepository repository, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var capability = await repository.UpdateAsync(siteId, spaceId, capabilityId, request.Value);
                if (capability == null) return ErrorResponses.NotFound("Space capability", capabilityId);
                logger.LogInformation("Updated capability {CapabilityId} for space {SpaceId}", capabilityId, spaceId);
                return Results.Ok(capability);
            }, logger, "update space capability", new { siteId, spaceId, capabilityId });
        })
        .WithName("UpdateSpaceCapability")
        .WithSummary("Update a space capability");

        capabilities.MapDelete("/{capabilityId:guid}", async (Guid siteId, Guid spaceId, Guid capabilityId, ISpaceCapabilityRepository repository, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await repository.DeleteAsync(siteId, spaceId, capabilityId);
                if (!deleted) return ErrorResponses.NotFound("Space capability", capabilityId);
                logger.LogInformation("Deleted capability {CapabilityId} from space {SpaceId}", capabilityId, spaceId);
                return Results.NoContent();
            }, logger, "delete space capability", new { siteId, spaceId, capabilityId });
        })
        .WithName("DeleteSpaceCapability")
        .WithSummary("Delete a space capability");
    }
}

public record AddSpaceCapabilityRequest(Guid CriterionId, object Value);
public record UpdateSpaceCapabilityRequest(object Value);
