using Api.Helpers;
using Api.Middleware;
using Api.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class GroupCapabilityEndpoints
{
    public static void MapGroupCapabilityEndpoints(this WebApplication app)
    {
        var capabilities = app.MapGroup("/api/resource-groups/{groupId:guid}/capabilities")
            .WithTags("Group Capabilities")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        capabilities.MapGet("/", async (Guid groupId, IGroupCapabilityRepository groupCapabilityRepository, CancellationToken ct) =>
        {
            var capabilitiesList = await groupCapabilityRepository.GetAllAsync(groupId, ct);
            return Results.Ok(capabilitiesList);
        })
        .WithName("GetGroupCapabilities")
        .WithSummary("Get all capabilities for a space group");

        capabilities.MapPost("/", async (Guid groupId, AddGroupCapabilityRequest request, IGroupCapabilityRepository groupCapabilityRepository, CancellationToken ct) =>
        {
            var capability = await groupCapabilityRepository.CreateAsync(groupId, request.CriterionId, request.Value, ct);
            return Results.Created($"/api/resource-groups/{groupId}/capabilities/{capability.Id}", capability);
        })
        .WithName("AddGroupCapability")
        .WithSummary("Add a capability to a resource group");

        capabilities.MapDelete("/{capabilityId:guid}", async (Guid groupId, Guid capabilityId, IGroupCapabilityRepository groupCapabilityRepository, CancellationToken ct) =>
        {
            var deleted = await groupCapabilityRepository.DeleteAsync(groupId, capabilityId, ct);
            return deleted ? Results.NoContent() : ErrorResponses.NotFound("Group capability", capabilityId);
        })
        .WithName("DeleteGroupCapability")
        .WithSummary("Remove a capability from a space group");
    }
}

public record AddGroupCapabilityRequest(Guid CriterionId, object Value);
