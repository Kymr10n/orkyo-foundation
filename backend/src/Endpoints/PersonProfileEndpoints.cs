using Api.Constants;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class PersonProfileEndpoints
{
    private const string NotAPersonMessage = "Resource is not a person";

    public static void MapPersonProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/person-profiles")
            .WithTags("PersonProfiles")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        // Bulk job-title labels for the utilization grid. POST (not GET) because a windowed grid can
        // hold hundreds of people and a repeated-query-param GET URL would exceed the request-line
        // limit; the body carries the id list. Returns only {resourceId, jobTitleName} — not the full
        // profile. AllowMemberWrite keeps this non-mutating POST open to readers.
        group.MapPost("/job-titles", async (
            [FromBody] Guid[] resourceIds,
            IPersonProfileRepository profileRepository,
            CancellationToken ct) =>
            Results.Ok(await profileRepository.GetJobTitlesByResourceIdsAsync(resourceIds, ct)))
            .WithName("ListPersonJobTitles")
            .WithSummary("Bulk job-title labels by resource IDs for the utilization grid")
            .AllowMemberWrite();

        // Bulk full profiles for the People list grid, replacing a per-row GET fan-out. POST (not GET)
        // for the same reason as /job-titles: the id list rides in the body so a large grid can't blow
        // the request-line limit. Resources without a profile row are simply absent from the result.
        // AllowMemberWrite keeps this non-mutating POST open to readers.
        group.MapPost("/batch", async (
            [FromBody] Guid[] resourceIds,
            IPersonProfileRepository profileRepository,
            CancellationToken ct) =>
            Results.Ok(await profileRepository.GetByResourceIdsAsync(resourceIds, ct)))
            .WithName("ListPersonProfiles")
            .WithSummary("Bulk person profiles by resource IDs for the People list")
            .AllowMemberWrite();

        group.MapGet("/{resourceId:guid}", async (
            Guid resourceId,
            IResourceRepository resourceRepository,
            IResourceTypeRepository resourceTypeRepository,
            IPersonProfileRepository profileRepository,
            CancellationToken ct) =>
        {
            var resolution = await ResolvePersonResourceAsync(resourceId, resourceRepository, resourceTypeRepository, ct);
            if (resolution.ErrorResult is not null) return resolution.ErrorResult;

            var profile = await profileRepository.GetByResourceIdAsync(resourceId, ct);
            return profile is null ? ErrorResponses.NotFound("Person profile") : Results.Ok(profile);
        })
            .WithName("GetPersonProfile")
            .WithSummary("Get a person profile by resource ID");

        group.MapPut("/{resourceId:guid}", async (
            Guid resourceId,
            [FromBody] UpsertPersonProfileRequest request,
            IResourceRepository resourceRepository,
            IResourceTypeRepository resourceTypeRepository,
            IPersonProfileRepository profileRepository,
            IValidator<UpsertPersonProfileRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var resolution = await ResolvePersonResourceAsync(resourceId, resourceRepository, resourceTypeRepository);
                if (resolution.ErrorResult is not null) return resolution.ErrorResult;

                var profile = await profileRepository.UpsertAsync(resourceId, request, ct);
                return Results.Ok(profile);
            }, logger, "upsert person profile", new { resourceId }))
            .WithName("UpsertPersonProfile")
            .WithSummary("Upsert a person profile");

        group.MapPost("/{resourceId:guid}/link", async (
            Guid resourceId,
            [FromBody] LinkUserToPersonProfileRequest request,
            IValidator<LinkUserToPersonProfileRequest> validator,
            IResourceRepository resourceRepository,
            IResourceTypeRepository resourceTypeRepository,
            IPersonProfileRepository profileRepository,
            CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
        {
            var resolution = await ResolvePersonResourceAsync(resourceId, resourceRepository, resourceTypeRepository, ct);
            if (resolution.ErrorResult is not null) return resolution.ErrorResult;

            // Reject if the user is already linked to a different person resource (tenant-wide constraint).
            var existingProfile = await profileRepository.GetByLinkedUserIdAsync(request.UserId, ct);
            if (existingProfile is not null && existingProfile.ResourceId != resourceId)
                return ErrorResponses.Conflict("User is already linked to another person profile");

            var success = await profileRepository.LinkUserAsync(resourceId, request.UserId, ct);
            return success ? Results.NoContent() : ErrorResponses.NotFound("PersonProfile", resourceId);
        }))
            .WithName("LinkUserToPersonProfile")
            .WithSummary("Link a user to a person profile");

        group.MapDelete("/{resourceId:guid}/link", async (
            Guid resourceId,
            IResourceRepository resourceRepository,
            IResourceTypeRepository resourceTypeRepository,
            IPersonProfileRepository profileRepository,
            CancellationToken ct) =>
        {
            var resolution = await ResolvePersonResourceAsync(resourceId, resourceRepository, resourceTypeRepository, ct);
            if (resolution.ErrorResult is not null) return resolution.ErrorResult;

            var success = await profileRepository.UnlinkUserAsync(resourceId, ct);
            return success ? Results.NoContent() : ErrorResponses.NotFound("Person profile");
        })
            .WithName("UnlinkUserFromPersonProfile")
            .WithSummary("Unlink a user from a person profile");
    }

    /// <summary>
    /// Loads the target resource and returns either the (typed) resource or a ready-to-return error result.
    /// All four endpoints share this preamble, so it lives here once.
    /// </summary>
    private static async Task<(ResourceInfo? Resource, IResult? ErrorResult)> ResolvePersonResourceAsync(
        Guid resourceId,
        IResourceRepository resourceRepository,
        IResourceTypeRepository resourceTypeRepository,
        CancellationToken ct = default)
    {
        var resource = await resourceRepository.GetByIdAsync(resourceId, ct);
        if (resource is null)
            return (null, ErrorResponses.NotFound("Resource", resourceId));

        // The flag, not the key: a tenant type that declares a directory profile carries the
        // same columns and belongs on this endpoint.
        var resourceType = await resourceTypeRepository.GetByKeyAsync(resource.ResourceTypeKey, ct);
        if (resourceType is null || !resourceType.HasDirectoryProfile)
            return (null, ErrorResponses.BadRequest(NotAPersonMessage));

        return (resource, null);
    }
}
