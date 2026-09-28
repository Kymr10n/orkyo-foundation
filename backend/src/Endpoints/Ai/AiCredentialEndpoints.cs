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

        // A key without the provider's prefix throws ArgumentException: AppExceptionHandler
        // renders it as a 400 validation error.
        return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            Results.Ok(await credentials.SaveAsync(request.ApiKey, principal.UserIdOrNull, ct)), ct);
    }

    private static async Task<IResult> DeleteCredential(
        IAiCredentialService credentials,
        ICurrentPrincipal principal,
        CancellationToken ct)
    {
        await credentials.DeleteAsync(principal.UserIdOrNull, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TestCredential(
        IAiCredentialService credentials,
        IAnthropicGateway gateway,
        ICurrentPrincipal principal,
        CancellationToken ct)
    {
        var apiKey = await credentials.GetApiKeyAsync(ct);
        if (string.IsNullOrEmpty(apiKey))
            return Results.Ok(new AiCredentialTestResult { Ok = false, Reason = "not_configured" });

        var result = await gateway.TestAsync(apiKey, AiDefaults.Model, ct);
        if (result.Ok) await credentials.MarkVerifiedAsync(ct);

        // Key saves and removals were audited; tests were not, though the constant existed.
        await credentials.RecordTestedAsync(result.Ok, result.Reason,
            principal.UserIdOrNull, ct);
        return Results.Ok(result);
    }
}
