using Api.Middleware;
using Api.Helpers;
using Api.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class GroupCapabilityEndpoints
{
    public static void MapGroupCapabilityEndpoints(this WebApplication app)
    {
        var capabilities = app.MapGroup("/api/groups/{groupId:guid}/capabilities")
            .WithTags("Group Capabilities")
            .RequireAuthorization()
            .RequireTenantMembership();

        capabilities.MapGet("/", async (Guid groupId, IGroupCapabilityRepository groupCapabilityRepository, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var capabilitiesList = await groupCapabilityRepository.GetAllAsync(groupId);
                return Results.Ok(capabilitiesList);
            }, logger, "get group capabilities", new { groupId });
        })
        .WithName("GetGroupCapabilities")
        .WithSummary("Get all capabilities for a space group");

        capabilities.MapPost("/", async (Guid groupId, AddGroupCapabilityRequest request, IGroupCapabilityRepository groupCapabilityRepository, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var capability = await groupCapabilityRepository.CreateAsync(groupId, request.CriterionId, request.Value);
                return Results.Created($"/api/groups/{groupId}/capabilities/{capability.Id}", capability);
            }, logger, "add group capability", new { groupId, criterionId = request.CriterionId });
        })
        .WithName("AddGroupCapability")
        .WithSummary("Add a capability to a space group");

        capabilities.MapDelete("/{capabilityId:guid}", async (Guid groupId, Guid capabilityId, IGroupCapabilityRepository groupCapabilityRepository, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await groupCapabilityRepository.DeleteAsync(groupId, capabilityId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Group capability", capabilityId);
            }, logger, "delete group capability", new { groupId, capabilityId });
        })
        .WithName("DeleteGroupCapability")
        .WithSummary("Remove a capability from a space group");
    }
}

public record AddGroupCapabilityRequest(Guid CriterionId, object Value);
