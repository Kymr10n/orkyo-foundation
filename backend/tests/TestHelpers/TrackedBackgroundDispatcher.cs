using System.Collections.Concurrent;
using Api.Security;
using Api.Services;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// Every run the test host's <see cref="IBackgroundDispatcher"/> has queued, so a test can await
/// work that runs after the response instead of polling for its effect.
/// </summary>
public sealed class BackgroundWorkTracker
{
    private readonly ConcurrentDictionary<Task, byte> _running = new();

    internal void Track(Task run)
    {
        _running[run] = 0;
        run.ContinueWith(t => _running.TryRemove(t, out _), TaskScheduler.Default);
    }

    /// <summary>Completes once every dispatch queued so far has run; fails after ten seconds.</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_running.Keys).WaitAsync(TimeSpan.FromSeconds(10));
}

/// <summary>
/// The production <see cref="BackgroundDispatcher"/>, with each run handed to the
/// <see cref="BackgroundWorkTracker"/>: same scope, same tenant, same suppressed flow.
/// </summary>
public sealed class TrackedBackgroundDispatcher(
    BackgroundDispatcher inner, CurrentTenant currentTenant, BackgroundWorkTracker tracker) : IBackgroundDispatcher
{
    public void Dispatch<TService>(string what, Func<TService, CancellationToken, Task> work) where TService : notnull
    {
        var tenant = currentTenant.GetTenantContext();
        using (ExecutionContext.SuppressFlow())
            tracker.Track(Task.Run(() => inner.RunAsync(tenant, what, work)));
    }
}
