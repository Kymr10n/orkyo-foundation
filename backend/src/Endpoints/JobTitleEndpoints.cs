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

public static class JobTitleEndpoints
{
    public static void MapJobTitleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/job-titles")
            .WithTags("JobTitles")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/", async (
            bool? includeInactive,
            IJobTitleRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await repo.GetAllAsync(includeInactive ?? false)),
                logger, "list job titles"))
            .WithName("GetJobTitles")
            .WithSummary("List all job titles");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IJobTitleRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var jt = await repo.GetByIdAsync(id);
                return EndpointHelpers.OkOrNotFound(jt, "JobTitle", id);
            }, logger, "get job title", new { id }))
            .WithName("GetJobTitle");

        group.MapPost("/", async (
            [FromBody] CreateJobTitleRequest request,
            IJobTitleRepository repo,
            IValidator<CreateJobTitleRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var jt = await repo.CreateAsync(request);
                return Results.Created($"/api/job-titles/{jt.Id}", jt);
            }, logger, "create job title"))
            .RequireAdminAccess()
            .WithName("CreateJobTitle");

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateJobTitleRequest request,
            IJobTitleRepository repo,
            IValidator<UpdateJobTitleRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var jt = await repo.UpdateAsync(id, request);
                return EndpointHelpers.OkOrNotFound(jt, "JobTitle", id);
            }, logger, "update job title", new { id }))
            .RequireAdminAccess()
            .WithName("UpdateJobTitle");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IJobTitleRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await repo.DeleteAsync(id);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("JobTitle", id);
            }, logger, "delete job title", new { id }))
            .RequireAdminAccess()
            .WithName("DeleteJobTitle");
    }
}
