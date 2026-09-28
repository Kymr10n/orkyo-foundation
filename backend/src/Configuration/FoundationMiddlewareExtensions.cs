using System.Diagnostics.CodeAnalysis;
using Api.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Api.Configuration;

[ExcludeFromCodeCoverage(Justification = "Pure middleware registration glue — no logic to unit-test")]
public static class FoundationMiddlewareExtensions
{
    /// <summary>
    /// Adds foundation-owned middleware in the correct order. Call this at the start of
    /// the product middleware pipeline, before CORS, auth, and product-specific middleware.
    /// Products must call <c>services.AddResponseCompression()</c> before <c>app.UseResponseCompression()</c>
    /// separately, as that is infrastructure — not foundation domain.
    /// </summary>
    /// <remarks>
    /// Request logging sits outside the exception handler, so it logs the status the client got.
    /// Inside it, every <c>NotFoundException</c>, <c>ConflictException</c> and
    /// <c>ArgumentException</c> reached it first and was logged at Error with a stack trace
    /// before <c>AppExceptionHandler</c> turned it into a 4xx.
    /// </remarks>
    public static WebApplication UseFoundationMiddleware(this WebApplication app)
    {
        app.UseCorrelationId();
        app.UseRequestLogging();
        app.UseExceptionHandler();
        return app;
    }
}
