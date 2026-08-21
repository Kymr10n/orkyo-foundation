using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Services.Ai;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Ai;

/// <summary>
/// Per-user assistant grants and monthly token budgets.
///
/// A workspace member cannot use the assistant until an admin grants them an allowance
/// here. Admins themselves need no grant — see <see cref="IAiAccessService"/>.
/// </summary>
public static class AiAllowanceEndpoints
{
    public static void MapAiAllowanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/allowances")
            .RequireAuthorization()
            .RequireAdminArea()
            .WithTags("AI Assistant");

        group.MapGet("/", ListAllowances)
            .WithName("ListAiAllowances")
            .WithSummary("List every workspace member with their assistant grant and this month's usage");

        group.MapPut("/{userId:guid}", SaveAllowance)
            .WithName("SaveAiAllowance")
            .WithSummary("Grant a user assistant access with a monthly token limit");

        group.MapDelete("/{userId:guid}", RevokeAllowance)
            .WithName("RevokeAiAllowance")
            .WithSummary("Revoke a user's assistant access entirely");
    }

    private static async Task<IResult> ListAllowances(
        IAiAccessService access,
        CancellationToken ct)
        => Results.Ok(await access.ListAllowancesAsync(ct));

    private static async Task<IResult> SaveAllowance(
        Guid userId,
        SaveAiAllowanceRequest request,
        IValidator<SaveAiAllowanceRequest> validator,
        IAiAccessService access,
        ICurrentPrincipal principal,
        CancellationToken ct)
    {
        var shape = await validator.ValidateAsync(request, ct);
        if (!shape.IsValid)
            return ProblemResults.Problem(StatusCodes.Status400BadRequest,
                Api.Constants.ErrorCodes.ValidationError,
                detail: "One or more fields failed validation.", errors: shape.ToDictionary());

        try
        {
            await access.SetAllowanceAsync(
                userId, request.MonthlyTokenLimit,
                principal.UserId == Guid.Empty ? null : principal.UserId, ct);
            return Results.NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ProblemResults.Problem(StatusCodes.Status400BadRequest,
                Api.Constants.ErrorCodes.ValidationError, detail: ex.Message);
        }
    }

    private static async Task<IResult> RevokeAllowance(
        Guid userId,
        IAiAccessService access,
        ICurrentPrincipal principal,
        CancellationToken ct)
    {
        var revoked = await access.RevokeAllowanceAsync(
            userId, principal.UserId == Guid.Empty ? null : principal.UserId, ct);
        return revoked ? Results.NoContent() : Results.NotFound();
    }
}
