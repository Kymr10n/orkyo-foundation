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
    /// The single rendering of a FluentValidation failure: the canonical problem body with the
    /// per-field messages in <c>errors</c>. Framework <c>Results.ValidationProblem</c> emits a
    /// ProblemDetails without our <c>code</c> extension, which would leave the frontend unable to
    /// switch on it — the whole reason #96 consolidated the shapes. Public because handlers that
    /// validate outside <see cref="ExecuteAsync"/> were re-implementing it inline.
    /// </summary>
    public static IResult ValidationFailed(FluentValidation.Results.ValidationResult result)
        => ProblemResults.Problem(
            StatusCodes.Status400BadRequest,
            Api.Constants.ErrorCodes.ValidationError,
            detail: "One or more fields failed validation.",
            errors: result.ToDictionary());

    /// <summary>
    /// Return <c>200 OK</c> with <paramref name="value"/>, or a standard <c>404 Not Found</c>
    /// (via <see cref="ErrorResponses.NotFound(string, Guid?)"/>) when it is null. Replaces the
    /// repeated <c>value is null ? ErrorResponses.NotFound(...) : Results.Ok(value)</c> ternary.
    /// </summary>
    public static IResult OkOrNotFound<T>(T? value, string resourceType, Guid? id = null) where T : class
        => value is null ? ErrorResponses.NotFound(resourceType, id) : Results.Ok(value);

    /// <summary>
    /// The delete/update counterpart of <see cref="OkOrNotFound"/>: 204 when the row was
    /// there, a canonical 404 when it was not. Twenty-two handlers wrote this ternary out.
    /// </summary>
    public static IResult NoContentOrNotFound(bool found, string resourceType, Guid? id = null)
        => found ? Results.NoContent() : ErrorResponses.NotFound(resourceType, id);

    /// <summary>
    /// Validate request with FluentValidation and execute handler, logging rejected
    /// requests under <paramref name="operationName"/>. Use the simpler overload below
    /// when the endpoint has no interest in logging validation rejections.
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
        {
            logger.LogWarning(
                "Validation failed for {OperationName}: {ErrorCount} error(s) on {Properties} (context: {@Context})",
                operationName,
                validationResult.Errors.Count,
                string.Join(", ", validationResult.Errors.Select(e => e.PropertyName).Distinct()),
                context);
            return ValidationFailed(validationResult);
        }

        return await handler();
    }

    /// <summary>
    /// Validate request with FluentValidation and execute handler (simplified, no logger required).
    /// The handler's return value is wrapped in <c>Results.Ok</c> — see the overload below for a
    /// handler that already produces its own <see cref="IResult"/> (Created, NoContent, …).
    /// </summary>
    public static async Task<IResult> ExecuteAsync<TRequest, TResult>(
        TRequest request,
        IValidator<TRequest> validator,
        Func<Task<TResult>> handler)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return ValidationFailed(validationResult);

        var result = await handler();
        return Results.Ok(result);
    }

    /// <summary>
    /// Validate request with FluentValidation and execute a handler that returns its own
    /// <see cref="IResult"/>. Overload resolution prefers this over the <c>TResult</c> version,
    /// which would otherwise wrap an IResult inside <c>Results.Ok</c> and serialise the wrapper.
    /// </summary>
    public static async Task<IResult> ExecuteAsync<TRequest>(
        TRequest request,
        IValidator<TRequest> validator,
        Func<Task<IResult>> handler)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return ValidationFailed(validationResult);

        return await handler();
    }
}
