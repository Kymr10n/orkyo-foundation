using Api.Constants;
using Api.Hosting;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Hosting;

/// <summary>
/// The worker host is a composition root, so most of it is exempt from coverage. Two things
/// are behaviour and are tested here: the argument guards, and the JOB ORDER — SaaS relies on
/// its tenant-lifecycle job running before the two shared defaults, which is the property that
/// broke when the two editions maintained the list separately. A third is REGISTRATION order:
/// the edition composes over the shared graph, so its registration must be the surviving one.
/// </summary>
public class OrkyoWorkerHostTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Mock.Of<IAnnouncementBroadcastService>());
        // UserLifecycleService is concrete and its ctor dereferences nothing, so real
        // instances over mocked collaborators are simpler than mocking the class itself.
        // BuildJobs only captures it; none of these tests run a job body.
        services.AddSingleton(Mock.Of<IDbConnectionFactory>());
        services.AddSingleton(Mock.Of<IServiceScopeFactory>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<UserLifecycleService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_RejectsABlankServiceName()
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            OrkyoWorkerHost.RunAsync("  ", [], (_, _) => { }, _ => []));

    [Fact]
    public async Task RunAsync_RejectsNullCollaborators()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            OrkyoWorkerHost.RunAsync("w", null!, (_, _) => { }, _ => []));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            OrkyoWorkerHost.RunAsync("w", [], null!, _ => []));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            OrkyoWorkerHost.RunAsync("w", [], (_, _) => { }, null!));
    }

    [Fact]
    public void BuildJobs_PutsTheEditionsJobsBeforeTheSharedOnes()
    {
        using var sp = BuildProvider();
        var extra = new WorkerJob("edition-job", (_, _) => true, _ => Task.CompletedTask);

        var names = OrkyoWorkerHost.BuildJobs(sp, _ => [extra]).Select(j => j.Name).ToArray();

        Assert.Equal(
            ["edition-job", WorkerJobNames.UserLifecycle, WorkerJobNames.AnnouncementBroadcast],
            names);
    }

    [Fact]
    public void BuildJobs_WithNoEditionJobs_YieldsOnlyTheSharedPair()
    {
        using var sp = BuildProvider();

        var names = OrkyoWorkerHost.BuildJobs(sp, _ => []).Select(j => j.Name).ToArray();

        Assert.Equal([WorkerJobNames.UserLifecycle, WorkerJobNames.AnnouncementBroadcast], names);
    }

    [Fact]
    public void BuildJobs_RunsTheAnnouncementBroadcastOnEveryPass()
    {
        using var sp = BuildProvider();

        var announcements = OrkyoWorkerHost.BuildJobs(sp, _ => [])
            .Single(j => j.Name == WorkerJobNames.AnnouncementBroadcast);

        Assert.True(announcements.IsDue(DateTime.UtcNow, DateTime.UtcNow));
    }
    /// <summary>
    /// The edition registers AFTER the shared graph, so it can replace a foundation service.
    /// Most of AddFoundationWorkerServices uses plain AddSingleton, where the last registration
    /// wins; with the order reversed a product's own IEmailService was silently discarded.
    /// </summary>
    [Fact]
    public void ComposeServices_LetsTheEditionReplaceASharedService()
    {
        var editionEmail = Mock.Of<IEmailService>();
        var services = new ServiceCollection();
        var context = new HostBuilderContext(new Dictionary<object, object>())
        {
            // AddFoundationWorkerServices builds KeycloakOptions eagerly, so the four keys it
            // requires have to be present. Values are irrelevant; nothing here connects.
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.KeycloakUrl] = "http://keycloak.invalid",
                [ConfigKeys.KeycloakRealm] = "orkyo",
                [ConfigKeys.KeycloakBackendClientId] = "backend",
                [ConfigKeys.KeycloakBackendClientSecret] = "secret",
            }).Build(),
        };

        OrkyoWorkerHost.ComposeServices(
            context,
            services,
            (_, s) => s.AddSingleton(editionEmail),
            _ => []);

        var resolved = services
            .Where(d => d.ServiceType == typeof(IEmailService))
            .Select(d => d.ImplementationInstance)
            .Last();
        Assert.Same(editionEmail, resolved);
    }
}
