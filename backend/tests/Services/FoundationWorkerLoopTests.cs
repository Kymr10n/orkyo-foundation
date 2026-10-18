using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The loop both editions' workers now share. The coordinator is faked so these pin the loop
/// itself: every job runs once per cycle in declaration order, a failing job is one Failed
/// outcome that neither stops the jobs after it nor changes the cadence, a journal outage is
/// retried after the error delay, and cancellation ends it cleanly.
///
/// The sleep is a <see cref="FakeTimeProvider"/>, so a cycle boundary is an explicit
/// <c>Advance</c> rather than a wall-clock wait: advancing one tick short of a delay proves
/// the loop is still asleep, and the tick after it proves what woke it.
/// </summary>
public sealed class FoundationWorkerLoopTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromMilliseconds(1);

    private sealed class FakeCoordinator : IWorkerJobCoordinator
    {
        public List<string> Ran { get; } = [];
        public Func<string, WorkerJobOutcome>? Outcome { get; init; }

        /// <summary>Throws from the coordinator itself: the journal connection is down.</summary>
        public Func<bool>? JournalDown { get; init; }

        public async Task<WorkerJobOutcome> RunIfDueAsync(
            string jobName, Func<DateTime, DateTime, bool> isDue, Func<CancellationToken, Task> job, CancellationToken cancellationToken)
        {
            if (JournalDown?.Invoke() == true) throw new InvalidOperationException("journal unreachable");
            Ran.Add(jobName);
            if (!isDue(DateTime.UtcNow, DateTime.MinValue)) return WorkerJobOutcome.NotDue;
            // Mirrors the real coordinator: a job's exception is journaled and reported, never thrown.
            try { await job(cancellationToken); }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { return WorkerJobOutcome.Failed; }
            return Outcome?.Invoke(jobName) ?? WorkerJobOutcome.Ran;
        }
    }

    private static WorkerJob Job(string name, Func<CancellationToken, Task>? run = null, bool due = true) =>
        new(name, (_, _) => due, run ?? (_ => Task.CompletedTask));

    [Fact]
    public async Task RunsEveryJobInOrder_ThenSleepsTheLoopDelay()
    {
        var coordinator = new FakeCoordinator();
        var cts = new CancellationTokenSource();
        var time = new FakeTimeProvider();
        var loop = new FoundationWorkerLoop(
            coordinator, [Job("a"), Job("b"), Job("c")], NullLogger<FoundationWorkerLoop>.Instance, time);

        var run = loop.RunAsync(cts.Token);

        coordinator.Ran.Should().Equal("a", "b", "c");

        // One tick short of the shortest possible loop delay (zero jitter): still asleep.
        time.Advance(WorkerSchedulePolicy.GetLoopDelay(TimeSpan.Zero) - OneTick);
        coordinator.Ran.Should().HaveCount(3, "the loop sleeps at least the loop delay");

        cts.Cancel(); // one cycle is enough
        await run;
    }

    [Fact]
    public async Task AFailingJob_DoesNotStopTheJobsAfterIt_AndKeepsTheNormalCadence()
    {
        // The October 2026 incident: user-lifecycle threw every cycle for four days and the
        // announcement job after it never ran. Now the failure is the coordinator's outcome.
        var coordinator = new FakeCoordinator();
        var cts = new CancellationTokenSource();
        var time = new FakeTimeProvider();
        var boom = Job("boom", _ => throw new InvalidOperationException("boom"));
        var loop = new FoundationWorkerLoop(
            coordinator, [boom, Job("after")], NullLogger<FoundationWorkerLoop>.Instance, time);

        var run = loop.RunAsync(cts.Token);

        coordinator.Ran.Should().Equal("boom", "after");
        time.Advance(WorkerSchedulePolicy.GetLoopDelay(TimeSpan.Zero) - OneTick);
        coordinator.Ran.Should().HaveCount(2, "a failed job does not shorten the sleep to the error delay");
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task AJournalOutage_WaitsTheErrorRetryDelay_ThenRetries()
    {
        var down = true;
        var coordinator = new FakeCoordinator { JournalDown = () => down };
        var cts = new CancellationTokenSource();
        var time = new FakeTimeProvider();
        var loop = new FoundationWorkerLoop(
            coordinator, [Job("a")], NullLogger<FoundationWorkerLoop>.Instance, time);

        var run = loop.RunAsync(cts.Token);

        coordinator.Ran.Should().BeEmpty("the journal was unreachable");
        down = false;
        time.Advance(WorkerSchedulePolicy.GetErrorRetryDelay() - OneTick);
        coordinator.Ran.Should().BeEmpty("the error retry delay has not elapsed");
        time.Advance(OneTick);
        coordinator.Ran.Should().Equal("a");
        cts.Cancel();
        await run;
    }

    [Fact]
    public async Task ANotDueJob_IsSkippedWithoutRunning()
    {
        var ran = false;
        var coordinator = new FakeCoordinator();
        var cts = new CancellationTokenSource();
        var loop = new FoundationWorkerLoop(
            coordinator,
            [Job("nightly", _ => { ran = true; return Task.CompletedTask; }, due: false)],
            NullLogger<FoundationWorkerLoop>.Instance, new FakeTimeProvider());

        var run = loop.RunAsync(cts.Token);
        cts.Cancel();
        await run;

        ran.Should().BeFalse();
    }

    [Fact]
    public void RejectsAnEmptyJobList()
    {
        var act = () => new FoundationWorkerLoop(
            new FakeCoordinator(), [], NullLogger<FoundationWorkerLoop>.Instance, TimeProvider.System);

        act.Should().Throw<ArgumentException>();
    }
}
