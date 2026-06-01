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
    public static WebApplication UseFoundationMiddleware(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseCorrelationId();
        app.UseRequestLogging();
        return app;
    }
}
