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

public static class DepartmentEndpoints
{
    public static void MapDepartmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/departments")
            .WithTags("Departments")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/", async (
            bool? includeInactive,
            IDepartmentRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await repo.GetAllAsync(includeInactive ?? false)),
                logger, "list departments"))
            .WithName("GetDepartments")
            .WithSummary("List all departments (flat)");

        group.MapGet("/tree", async (
            bool? includeInactive,
            IDepartmentRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await repo.GetTreeAsync(includeInactive ?? false)),
                logger, "list departments as tree"))
            .WithName("GetDepartmentTree")
            .WithSummary("List all departments as a nested tree");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDepartmentRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var d = await repo.GetByIdAsync(id);
                return d is null ? ErrorResponses.NotFound("Department", id) : Results.Ok(d);
            }, logger, "get department", new { id }))
            .WithName("GetDepartment");

        group.MapPost("/", async (
            [FromBody] CreateDepartmentRequest request,
            IDepartmentRepository repo,
            IValidator<CreateDepartmentRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var d = await repo.CreateAsync(request);
                return Results.Created($"/api/departments/{d.Id}", d);
            }, logger, "create department"))
            .RequireAdminAccess()
            .WithName("CreateDepartment");

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateDepartmentRequest request,
            IDepartmentRepository repo,
            IValidator<UpdateDepartmentRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var d = await repo.UpdateAsync(id, request);
                return d is null ? ErrorResponses.NotFound("Department", id) : Results.Ok(d);
            }, logger, "update department", new { id }))
            .RequireAdminAccess()
            .WithName("UpdateDepartment");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDepartmentRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await repo.DeleteAsync(id);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Department", id);
            }, logger, "delete department", new { id }))
            .RequireAdminAccess()
            .WithName("DeleteDepartment");
    }
}
