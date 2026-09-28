using Api.Security;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Best-effort work (audit rows, notification mails) used to run on the request's own services
/// with the request's token, so it raced the scope's disposal and a client disconnect. The
/// dispatcher runs it in a fresh scope, uncancellable, with the request's tenant carried over.
/// </summary>
public class BackgroundDispatcherTests
{
    private static readonly TenantContext Tenant = new()
    {
        TenantId = Guid.NewGuid(),
        TenantSlug = "acme",
        TenantDbConnectionString = "Host=localhost;Database=acme",
        Status = "active",
    };

    /// <summary>A scoped service that records what it saw when the work ran.</summary>
    private sealed class Probe(ICurrentTenant tenant) : IDisposable
    {
        public bool Disposed { get; private set; }
        public Guid SeenTenant => tenant.TenantId;
        public void Dispose() => Disposed = true;
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
        services.AddScoped<Probe>();
        services.AddScoped<IBackgroundDispatcher>(sp => new BackgroundDispatcher(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<CurrentTenant>(),
            NullLogger<BackgroundDispatcher>.Instance));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task TheWork_OutlivesTheRequestScope_AndKeepsItsTenant()
    {
        using var root = Services();
        var ran = new TaskCompletionSource<(Guid Tenant, bool Disposed, bool Cancellable)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource();

        var request = root.CreateScope();
        request.ServiceProvider.GetRequiredService<CurrentTenant>().SetContext(Tenant);
        request.ServiceProvider.GetRequiredService<IBackgroundDispatcher>().Dispatch<Probe>("probe", async (probe, ct) =>
        {
            await release.Task; // runs only after the request scope is gone
            ran.SetResult((probe.SeenTenant, probe.Disposed, ct.CanBeCanceled));
        });
        request.Dispose();
        release.SetResult();

        var seen = await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
        seen.Tenant.Should().Be(Tenant.TenantId);
        seen.Disposed.Should().BeFalse("the work gets its own scope, not the disposed request's");
        seen.Cancellable.Should().BeFalse("a client disconnect must not cancel an audit row");
    }

    [Fact]
    public async Task AFailure_IsLoggedNotThrown()
    {
        using var root = Services();
        using var scope = root.CreateScope();
        var dispatcher = (BackgroundDispatcher)scope.ServiceProvider.GetRequiredService<IBackgroundDispatcher>();

        var act = () => dispatcher.RunAsync<Probe>(null, "probe", (_, _) => throw new InvalidOperationException("smtp down"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WithoutATenant_TheWorkRunsInSiteScope()
    {
        using var root = Services();
        using var scope = root.CreateScope();
        var dispatcher = (BackgroundDispatcher)scope.ServiceProvider.GetRequiredService<IBackgroundDispatcher>();
        Guid? seen = null;

        await dispatcher.RunAsync<Probe>(null, "probe", (probe, _) => { seen = probe.SeenTenant; return Task.CompletedTask; });

        seen.Should().Be(Guid.Empty);
    }
}
