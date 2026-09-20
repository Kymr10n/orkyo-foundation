using Api.Configuration;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkyo.Shared;
using Serilog;

namespace Api.Hosting;

/// <summary>
/// The background worker process both editions run. Everything below was duplicated in
/// <c>worker/Program.cs</c> in each product (56 identical lines of 74 and 61): the Serilog
/// bootstrap, the tzdata probe, the generic host, the shared service graph, the two shared
/// jobs, the hosted shell, and the fatal-crash contract. A product's <c>Program.cs</c> is
/// now one <see cref="RunAsync"/> call.
/// </summary>
/// <remarks>
/// Logging contract (unchanged): Information default with Microsoft overridden to Warning,
/// stdout only — Alloy's docker log source ships every container's stdout to Loki, so a
/// rolling file sink only fills an ephemeral container layer that nothing collects. A fatal
/// crash logs <c>Log.Fatal</c> and returns 1, so restart policies see the crash instead of a
/// clean exit; the logger is always flushed.
/// </remarks>
public static class OrkyoWorkerHost
{
    private const string LogOutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Runs the worker until it stops and returns the process exit code (0 normal, 1 fatal).
    /// </summary>
    /// <param name="serviceName">Display name for the start and shutdown log lines.</param>
    /// <param name="args">The process arguments, passed to the host builder.</param>
    /// <param name="configureEditionServices">
    /// The edition's own registrations — its <c>IDbConnectionFactory</c> and any service its
    /// extra jobs resolve. Runs <em>after</em> the shared graph, so an edition can replace a
    /// foundation service. Most of <c>AddFoundationWorkerServices</c> uses plain
    /// <c>AddSingleton</c>, where the last registration wins; running first would mean a
    /// product's own <c>IEmailService</c> was silently overwritten by foundation's. This is the
    /// same "compose over the shared Add* extension and override" shape the test hosts use.
    /// Registration order never affects resolution, only which registration survives.
    /// </param>
    /// <param name="extraJobs">
    /// Jobs this edition adds to the two shared ones. They run first in each loop pass, which
    /// is the order both editions had (SaaS ran tenant lifecycle before user lifecycle).
    /// </param>
    public static async Task<int> RunAsync(
        string serviceName,
        string[] args,
        Action<HostBuilderContext, IServiceCollection> configureEditionServices,
        Func<IServiceProvider, IReadOnlyList<WorkerJob>> extraJobs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configureEditionServices);
        ArgumentNullException.ThrowIfNull(extraJobs);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: LogOutputTemplate)
            .CreateLogger();

        try
        {
            Log.Information("Starting {WorkerName}...", serviceName);

            // Fail fast if the runtime image lacks tzdata: auto-scheduling resolves each site's
            // IANA time zone, and a missing database must kill the container at boot rather than
            // fail per tenant in the middle of a run. Same probe the API runs via
            // ConfigurationValidator, which the workers have no --validate path for.
            if (ConfigurationValidator.TimeZoneDataError() is { } timeZoneError)
                throw new InvalidOperationException(timeZoneError);

            using var host = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureServices((context, services) =>
                    ComposeServices(context, services, configureEditionServices, extraJobs))
                .Build();

            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Worker terminated unexpectedly");
            // Surface the crash to the container/orchestrator — a clean exit 0 here would
            // make a dead worker look healthy to restart policies.
            return 1;
        }
        finally
        {
            Log.Information("{WorkerName} shutting down...", serviceName);
            await Log.CloseAndFlushAsync();
        }
    }

    // internal, not private: the job ORDER is a behavioural contract the products rely on
    // (saas expects its tenant-lifecycle job to run before the two defaults), so it is tested.
    /// <summary>
    /// The worker's service graph, in the order that decides which registration survives.
    /// Internal so a test can assert the edition's registration wins, which is the whole point
    /// of the ordering: most of <c>AddFoundationWorkerServices</c> uses plain
    /// <c>AddSingleton</c>, so whichever runs last is the one resolved.
    /// </summary>
    internal static void ComposeServices(
        HostBuilderContext context,
        IServiceCollection services,
        Action<HostBuilderContext, IServiceCollection> configureEditionServices,
        Func<IServiceProvider, IReadOnlyList<WorkerJob>> extraJobs)
    {
        // Shared worker graph (HTTP client, Keycloak, email, announcements, user lifecycle).
        services.AddFoundationWorkerServices(context.Configuration);
        // Edition last, so it can override any of the above. See the RunAsync parameter docs.
        configureEditionServices(context, services);
        services.AddFoundationWorkerLoop(sp => BuildJobs(sp, extraJobs));
        services.AddHostedService<WorkerHost>();
    }

    internal static IEnumerable<WorkerJob> BuildJobs(
        IServiceProvider sp, Func<IServiceProvider, IReadOnlyList<WorkerJob>> extraJobs)
    {
        var userLifecycle = sp.GetRequiredService<UserLifecycleService>();
        var announcements = sp.GetRequiredService<IAnnouncementBroadcastService>();
        var logger = sp.GetRequiredService<ILogger<WorkerHost>>();

        return
        [
            .. extraJobs(sp),
            // User lifecycle (GDPR inactivity management) once per day
            new WorkerJob(WorkerJobNames.UserLifecycle, WorkerSchedulePolicy.ShouldRunUserLifecycle, async ct =>
            {
                logger.LogInformation("Running user lifecycle check...");
                await userLifecycle.ProcessAsync(ct);
            }),
            // Announcement email broadcasts are time-sensitive — attempt every loop,
            // but single-flight across instances so replicas cannot double-send.
            new WorkerJob(WorkerJobNames.AnnouncementBroadcast, (_, _) => true,
                ct => announcements.ProcessPendingBroadcastsAsync(ct)),
        ];
    }
}

/// <summary>
/// The hosted shell around <see cref="FoundationWorkerLoop"/>: the loop (job journal, per-job
/// lock, jitter, error retry) lives in core; this only keeps it running for the host's lifetime.
/// </summary>
internal sealed class WorkerHost(FoundationWorkerLoop loop) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => loop.RunAsync(stoppingToken);
}
