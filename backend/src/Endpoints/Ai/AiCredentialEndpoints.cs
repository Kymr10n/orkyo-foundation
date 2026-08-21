using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Security.Features;
using Api.Services.Ai;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Ai;

/// <summary>
/// Administration of the workspace's AI provider key.
///
/// The key travels in exactly one direction: a workspace admin writes it, and from then
/// on only the server-side chat proxy reads it. No response on this surface — success or
/// error — ever contains the key or its ciphertext.
/// </summary>
public static class AiCredentialEndpoints
{
    public static void MapAiCredentialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/credentials")
            .RequireAuthorization()
            .RequireAdminArea()
            .WithTags("AI Assistant");

        group.MapGet("/", GetCredential)
            .WithName("GetAiCredential")
            .WithSummary("Report whether an AI key is configured (never returns the key)");

        group.MapPut("/", SaveCredential)
            .WithName("SaveAiCredential")
            .WithSummary("Store or replace the workspace's AI key");

        group.MapDelete("/", DeleteCredential)
            .WithName("DeleteAiCredential")
            .WithSummary("Remove the workspace's AI key and switch the assistant off");

        // Probing a key changes nothing, so it stays inside the admin area but is marked
        // non-mutating — it exists so an admin can tell a wrong key from a wrong network.
        group.MapPost("/test", TestCredential)
            .AllowMemberWrite()
            .WithName("TestAiCredential")
            .WithSummary("Check the stored key against the provider without spending tokens");
    }

    private static async Task<IResult> GetCredential(
        IAiCredentialService credentials,
        CancellationToken ct)
        => Results.Ok(await credentials.GetStatusAsync(ct));

    private static async Task<IResult> SaveCredential(
        SaveAiCredentialRequest request,
        IValidator<SaveAiCredentialRequest> validator,
        IAiCredentialService credentials,
        IFeatureGate featureGate,
        ICurrentPrincipal principal,
        CancellationToken ct)
    {
        // Storing a key is only meaningful where the assistant can run. The gate throws
        // FeatureNotAvailableException, which AppExceptionHandler renders as 403.
        await featureGate.EnsureEnabledAsync(FeatureKeys.AiAssistant, ct);

        var shape = await validator.ValidateAsync(request, ct);
        if (!shape.IsValid)
            return ProblemResults.Problem(StatusCodes.Status400BadRequest,
                Api.Constants.ErrorCodes.ValidationError,
                detail: "One or more fields failed validation.", errors: shape.ToDictionary());

        try
        {
            var status = await credentials.SaveAsync(
                request.ApiKey, principal.UserId == Guid.Empty ? null : principal.UserId, ct);
            return Results.Ok(status);
        }
        catch (ArgumentException ex)
        {
            return ProblemResults.Problem(StatusCodes.Status400BadRequest,
                Api.Constants.ErrorCodes.ValidationError, detail: ex.Message);
        }
    }

    private static async Task<IResult> DeleteCredential(
        IAiCredentialService credentials,
        ICurrentPrincipal principal,
        CancellationToken ct)
    {
        await credentials.DeleteAsync(principal.UserId == Guid.Empty ? null : principal.UserId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TestCredential(
        IAiCredentialService credentials,
        IAnthropicGateway gateway,
        CancellationToken ct)
    {
        var apiKey = await credentials.GetApiKeyAsync(ct);
        if (string.IsNullOrEmpty(apiKey))
            return Results.Ok(new AiCredentialTestResult { Ok = false, Reason = "not_configured" });

        var result = await gateway.TestAsync(apiKey, await credentials.GetModelAsync(ct), ct);
        if (result.Ok) await credentials.MarkVerifiedAsync(ct);
        return Results.Ok(result);
    }
}
