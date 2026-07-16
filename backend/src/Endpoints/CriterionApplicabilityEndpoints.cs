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
            .RequireMemberReadEditorWrite();

        group.MapGet("/{id:guid}/applicability", async (
            Guid id,
            ICriterionApplicabilityRepository repo,
            CancellationToken ct) =>
        {
            var info = await repo.GetByCriterionAsync(id);
            return EndpointHelpers.OkOrNotFound(info, "Criterion", id);
        })
            .WithName("GetCriterionApplicability")
            .WithSummary("Get applicability settings for a criterion");

        group.MapPut("/{id:guid}/applicability", async (
            Guid id,
            [FromBody] UpdateCriterionApplicabilityRequest request,
            ICriterionApplicabilityRepository applicabilityRepo,
            IResourceTypeRepository resourceTypeRepo,
            ICriteriaRepository criteriaRepo,
            CancellationToken ct) =>
        {
            var criterion = await criteriaRepo.GetByIdAsync(id);
            if (criterion is null)
                return ErrorResponses.NotFound("Criterion", id);

            if (request.ApplicableToRequests.HasValue)
                await applicabilityRepo.SetApplicableToRequestsAsync(id, request.ApplicableToRequests.Value);

            if (request.ResourceTypeKeys is not null)
            {
                // Resource types are a small catalog — one fetch replaces a per-key lookup loop.
                var typesByKey = (await resourceTypeRepo.GetAllAsync(ct)).ToDictionary(rt => rt.Key);
                var typeIds = new List<Guid>();
                foreach (var key in request.ResourceTypeKeys)
                {
                    if (!typesByKey.TryGetValue(key, out var rt))
                        return ErrorResponses.BadRequest($"Unknown resource type key '{key}'");
                    typeIds.Add(rt.Id);
                }
                await applicabilityRepo.SetResourceTypeApplicabilityAsync(id, typeIds);
            }

            var updated = await applicabilityRepo.GetByCriterionAsync(id);
            return Results.Ok(updated);
        })
            .WithName("UpdateCriterionApplicability")
            .WithSummary("Update applicability settings for a criterion");
    }
}
