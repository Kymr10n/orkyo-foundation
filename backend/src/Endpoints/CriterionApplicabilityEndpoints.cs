using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class CriterionApplicabilityEndpoints
{
    public static void MapCriterionApplicabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/criteria")
            .WithTags("Criteria")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/{id:guid}/applicability", async (
            Guid id,
            ICriterionApplicabilityRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var info = await repo.GetByCriterionAsync(id);
                return EndpointHelpers.OkOrNotFound(info, "Criterion", id);
            }, logger, "get criterion applicability", new { id }))
            .WithName("GetCriterionApplicability")
            .WithSummary("Get applicability settings for a criterion");

        group.MapPut("/{id:guid}/applicability", async (
            Guid id,
            [FromBody] UpdateCriterionApplicabilityRequest request,
            ICriterionApplicabilityRepository applicabilityRepo,
            IResourceTypeRepository resourceTypeRepo,
            ICriteriaRepository criteriaRepo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var criterion = await criteriaRepo.GetByIdAsync(id);
                if (criterion is null)
                    return ErrorResponses.NotFound("Criterion", id);

                if (request.ApplicableToRequests.HasValue)
                    await applicabilityRepo.SetApplicableToRequestsAsync(id, request.ApplicableToRequests.Value);

                if (request.ResourceTypeKeys is not null)
                {
                    var typeIds = new List<Guid>();
                    foreach (var key in request.ResourceTypeKeys)
                    {
                        var rt = await resourceTypeRepo.GetByKeyAsync(key);
                        if (rt is null)
                            return ErrorResponses.BadRequest($"Unknown resource type key '{key}'");
                        typeIds.Add(rt.Id);
                    }
                    await applicabilityRepo.SetResourceTypeApplicabilityAsync(id, typeIds);
                }

                var updated = await applicabilityRepo.GetByCriterionAsync(id);
                return Results.Ok(updated);
            }, logger, "update criterion applicability", new { id }))
            .RequireEditAccess()
            .WithName("UpdateCriterionApplicability")
            .WithSummary("Update applicability settings for a criterion");
    }
}
