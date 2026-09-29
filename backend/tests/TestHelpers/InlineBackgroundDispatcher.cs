using Api.Services;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// Runs dispatched work at once against the given service instances, so a hand-composed
/// service under test has sent its best-effort mail by the time its method returns.
/// </summary>
public sealed class InlineBackgroundDispatcher(params object[] services) : IBackgroundDispatcher
{
    public void Dispatch<TService>(string what, Func<TService, CancellationToken, Task> work) where TService : notnull
        => work(services.OfType<TService>().Single(), CancellationToken.None).GetAwaiter().GetResult();
}
