using Api.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Services;

/// <summary>
/// Runs best-effort work — a notification mail, an audit row — after the caller returns, without
/// holding up the response.
///
/// The work must not use the request's own services: the request scope, with its connections,
/// is disposed the moment the response completes, and the request's cancellation token fires when
/// the client disconnects. Either one silently dropped the work under load (the race
/// <c>BearerTokenAuthHandler</c> documents for its last-used touch). So the work runs in a fresh
/// DI scope, with <see cref="CancellationToken.None"/>, and with the caller's tenant carried over,
/// so a tenant-branded mail keeps its branding. A failure is logged, never thrown.
/// </summary>
public interface IBackgroundDispatcher
{
    /// <summary>
    /// Queues <paramref name="work"/> against a <typeparamref name="TService"/> resolved from a new
    /// scope. <paramref name="what"/> names the work in the failure log.
    /// </summary>
    void Dispatch<TService>(string what, Func<TService, CancellationToken, Task> work) where TService : notnull;
}

public sealed class BackgroundDispatcher(
    IServiceScopeFactory scopeFactory,
    CurrentTenant currentTenant,
    ILogger<BackgroundDispatcher> logger) : IBackgroundDispatcher
{
    public void Dispatch<TService>(string what, Func<TService, CancellationToken, Task> work) where TService : notnull
    {
        // Read now: by the time the work runs, the scope that holds the tenant may be gone.
        var tenant = currentTenant.GetTenantContext();

        // No request state flows into the work: it must not see the request's HttpContext,
        // which the server recycles once the response completes.
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(() => RunAsync(tenant, what, work));
    }

    /// <summary>The work itself; separate so tests can await it.</summary>
    internal async Task RunAsync<TService>(
        TenantContext? tenant, string what, Func<TService, CancellationToken, Task> work) where TService : notnull
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (tenant is not null)
                scope.ServiceProvider.GetRequiredService<CurrentTenant>().SetContext(tenant);
            await work(scope.ServiceProvider.GetRequiredService<TService>(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Nothing awaits this task, so an escaped exception would only be an unobserved-task event.
            logger.LogWarning(ex, "Background {What} failed", what);
        }
    }
}
