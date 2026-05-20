using Api.Integrations.Keycloak;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Api.Helpers;

/// <summary>
/// Wrapper for standardizing endpoint handlers with validation and null-to-404 coercion.
/// Exception mapping is handled globally by <see cref="AppExceptionHandler"/>.
///
/// Lives in <c>orkyo-foundation</c> because the validation envelope and try/catch wrapper
/// shape are identical across multi-tenant SaaS and single-tenant Community deployments.
/// </summary>
public static class EndpointHelpers
{
    /// <summary>
    /// Execute an async handler with standard error handling and logging
    /// </summary>
    public static async Task<IResult> ExecuteAsync(
        Func<Task<IResult>> handler,
        ILogger logger,
        string operationName,
        object? context = null)
    {
        return await handler();
    }

    /// <summary>
    /// Execute an async handler that returns data, mapping null to 404.
    /// </summary>
    public static async Task<IResult> ExecuteAsync<T>(
        Func<Task<T?>> handler,
        ILogger logger,
        string operationName,
        object? context = null) where T : class
    {
        var result = await handler();
        return result != null ? Results.Ok(result) : Results.NotFound();
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

        return await handler();
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

        var result = await handler();
        return Results.Ok(result);
    }
}
