using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services.AutoSchedule;
using FluentValidation;
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
            .RequireMemberReadEditorWrite();

        group.MapPost("/preview", async (AutoSchedulePreviewRequest request,
            IValidator<AutoSchedulePreviewRequest> validator,
            IAutoScheduleService service, CancellationToken cancellationToken) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
                Results.Ok(await service.PreviewAsync(request, cancellationToken))))
        .WithName("AutoSchedulePreview")
        .WithSummary("Compute auto-schedule proposal without persisting")
        .Produces<AutoSchedulePreviewResponse>(StatusCodes.Status200OK)
        .AllowMemberWrite();

        group.MapPost("/apply", async (AutoScheduleApplyRequest request,
            IValidator<AutoScheduleApplyRequest> validator,
            IAutoScheduleService service, CancellationToken cancellationToken) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
                Results.Ok(await service.ApplyAsync(request, cancellationToken))))
        .WithName("AutoScheduleApply")
        .WithSummary("Apply auto-schedule proposal")
        .Produces<AutoScheduleApplyResponse>(StatusCodes.Status200OK);
    }
}
