using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services.AutoSchedule;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class AutoScheduleEndpoints
{
    public static void MapAutoScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scheduling/auto-schedule")
            .WithTags("AutoSchedule")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapPost("/preview", async (AutoSchedulePreviewRequest request, IAutoScheduleService service, ILogger<EndpointLoggerCategory> logger, CancellationToken cancellationToken) =>
        {
            return await EndpointHelpers.ExecuteAsync(
                async () => Results.Ok(await service.PreviewAsync(request, cancellationToken)),
                logger, "auto-schedule preview");
        })
        .WithName("AutoSchedulePreview")
        .WithSummary("Compute auto-schedule proposal without persisting")
        .Produces<AutoSchedulePreviewResponse>(StatusCodes.Status200OK);

        group.MapPost("/apply", async (AutoScheduleApplyRequest request, IAutoScheduleService service, ILogger<EndpointLoggerCategory> logger, CancellationToken cancellationToken) =>
        {
            return await EndpointHelpers.ExecuteAsync(
                async () => Results.Ok(await service.ApplyAsync(request, cancellationToken)),
                logger, "auto-schedule apply");
        })
        .WithName("AutoScheduleApply")
        .WithSummary("Apply auto-schedule proposal")
        .Produces<AutoScheduleApplyResponse>(StatusCodes.Status200OK);
    }
}
