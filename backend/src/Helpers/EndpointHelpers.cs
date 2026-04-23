using Api.Constants;
using Api.Integrations.Keycloak;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Api.Helpers;

/// <summary>
/// Wrapper for standardizing endpoint handlers with try-catch and logging.
///
/// Lives in <c>orkyo-foundation</c> because the exception-to-HTTP routing,
/// validation envelope, and try/catch wrapper shape are identical across
/// multi-tenant SaaS and single-tenant Community deployments. The underlying
/// <see cref="ErrorResponses"/> contract and <see cref="KeycloakAdminExceptionMapper"/>
/// it dispatches to are also foundation-owned.
/// </summary>
public static class EndpointHelpers
{
    /// <summary>
    /// Central exception-to-IResult mapper — single source of truth for HTTP status routing.
    /// </summary>
    private static IResult MapExceptionToResult(Exception ex, ILogger? logger, string operationName)
    {
        return ex switch
        {
            InvalidOperationException ioe when ioe.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                => ErrorResponses.NotFound(ioe.Message, Guid.Empty),
            InvalidOperationException ioe
                => ErrorResponses.Conflict(ioe.Message),
            KeyNotFoundException knf
                => ErrorResponses.NotFound(knf.Message, Guid.Empty),
            ArgumentException arg
                => ErrorResponses.BadRequest(arg.Message),
            UnauthorizedAccessException
                => Results.Forbid(),
            KeycloakAdminException kae
                => KeycloakAdminExceptionMapper.Map(kae),
            Npgsql.PostgresException pg when pg.SqlState == "23505"
                => ErrorResponses.Conflict("A record with this identifier already exists"),
            _ => LogAndProblem(ex, logger, operationName)
        };
    }

    private static IResult LogAndProblem(Exception ex, ILogger? logger, string operationName)
    {
        // No logger provided — silent fallback. Composition layers that want a
        // global static fallback (e.g. Serilog.Log.Error) should always pass
        // their own ILogger.
        logger?.LogError(ex, "Failed to {Operation}", operationName);
        return Results.Problem($"Failed to {operationName}");
    }

    /// <summary>
    /// Execute an async handler with standard error handling and logging
    /// </summary>
    public static async Task<IResult> ExecuteAsync(
        Func<Task<IResult>> handler,
        ILogger logger,
        string operationName,
        object? context = null)
    {
        try
        {
            return await handler();
        }
        catch (Exception ex)
        {
            return MapExceptionToResult(ex, logger, operationName);
        }
    }

    /// <summary>
    /// Execute an async handler that returns data with standard error handling
    /// </summary>
    public static async Task<IResult> ExecuteAsync<T>(
        Func<Task<T?>> handler,
        ILogger logger,
        string operationName,
        object? context = null) where T : class
    {
        try
        {
            var result = await handler();
            return result != null ? Results.Ok(result) : Results.NotFound();
        }
        catch (Exception ex)
        {
            return MapExceptionToResult(ex, logger, operationName);
        }
    }

    /// <summary>
    /// Validate request with FluentValidation and execute handler with standard error handling
    /// </summary>
    public static async Task<IResult> ExecuteAsync<TRequest>(
        TRequest request,
        IValidator<TRequest> validator,
        Func<Task<IResult>> handler,
        ILogger logger,
        string operationName,
        object? context = null)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return Results.ValidationProblem(validationResult.ToDictionary());

        return await ExecuteAsync(handler, logger, operationName, context);
    }

    /// <summary>
    /// Validate request with FluentValidation and execute handler (simplified, no logger required)
    /// </summary>
    public static async Task<IResult> ExecuteAsync<TRequest, TResult>(
        TRequest request,
        IValidator<TRequest> validator,
        Func<Task<TResult>> handler)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return Results.ValidationProblem(validationResult.ToDictionary());

        try
        {
            var result = await handler();
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return MapExceptionToResult(ex, null, "process request");
        }
    }
}
