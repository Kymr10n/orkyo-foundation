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
            .RequireMemberReadEditorWrite();

        group.MapGet("/{id:guid}/members", async (
            Guid id,
            IResourceGroupMemberRepository repo,
            CancellationToken ct) =>
            Results.Ok(await repo.GetMembersAsync(id, ct)))
            .WithName("GetResourceGroupMembers")
            .WithSummary("Get all members of a resource group");

        group.MapPut("/{id:guid}/members", async (
            Guid id,
            [FromBody] SetResourceGroupMembersRequest request,
            IResourceGroupMemberRepository repo,
            CancellationToken ct) =>
        {
            await repo.SetMembersAsync(id, request.ResourceIds, ct);
            return Results.Ok(await repo.GetMembersAsync(id, ct));
        })
            .WithName("SetResourceGroupMembers")
            .WithSummary("Replace all members of a resource group");
    }
}
