using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class ResourceGroupMemberEndpoints
{
    public static void MapResourceGroupMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resource-groups")
            .WithTags("Resource Groups")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/{id:guid}/members", async (
            Guid id,
            IResourceGroupMemberRepository repo,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await repo.GetMembersAsync(id)),
            logger, "get resource group members", new { id }))
            .WithName("GetResourceGroupMembers")
            .WithSummary("Get all members of a resource group");

        group.MapPut("/{id:guid}/members", async (
            Guid id,
            [FromBody] SetResourceGroupMembersRequest request,
            IResourceGroupMemberRepository repo,
            ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                await repo.SetMembersAsync(id, request.ResourceIds);
                return Results.Ok(await repo.GetMembersAsync(id));
            }, logger, "set resource group members", new { id }))
            .RequireAdminAccess()
            .WithName("SetResourceGroupMembers")
            .WithSummary("Replace all members of a resource group");
    }
}
