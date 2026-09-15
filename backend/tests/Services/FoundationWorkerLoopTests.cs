using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The loop both editions' workers now share. The coordinator is faked so these pin the loop
/// itself: every job runs once per cycle in declaration order, a failing job does not stop
/// the loop and is retried after the error delay, and cancellation ends it cleanly.
/// </summary>
public sealed class FoundationWorkerLoopTests
{
    private sealed class FakeCoordinator : IWorkerJobCoordinator
    {
        public List<string> Ran { get; } = [];
        public Func<string, WorkerJobOutcome>? Outcome { get; init; }

        public async Task<WorkerJobOutcome> RunIfDueAsync(
            string jobName, Func<DateTime, DateTime, bool> isDue, Func<CancellationToken, Task> job, CancellationToken cancellationToken)
        {
            Ran.Add(jobName);
            if (!isDue(DateTime.UtcNow, DateTime.MinValue)) return WorkerJobOutcome.NotDue;
            await job(cancellationToken);
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
        var delays = new List<TimeSpan>();
        Task Delay(TimeSpan span, CancellationToken _)
        {
            delays.Add(span);
            cts.Cancel(); // one cycle is enough
            return Task.CompletedTask;
        }
        var loop = new FoundationWorkerLoop(coordinator, [Job("a"), Job("b"), Job("c")], NullLogger<FoundationWorkerLoop>.Instance, Delay);

        await loop.RunAsync(cts.Token);

        coordinator.Ran.Should().Equal("a", "b", "c");
        delays.Should().ContainSingle().Which.Should().BeGreaterThanOrEqualTo(WorkerSchedulePolicy.GetLoopDelay(TimeSpan.Zero));
    }

    [Fact]
    public async Task AFailingJob_DoesNotStopTheLoop_AndWaitsTheErrorRetryDelay()
    {
        var coordinator = new FakeCoordinator();
        var cts = new CancellationTokenSource();
        var delays = new List<TimeSpan>();
        var cycles = 0;
        Task Delay(TimeSpan span, CancellationToken _)
        {
            delays.Add(span);
            if (++cycles == 2) cts.Cancel();
            return Task.CompletedTask;
        }
        var boom = Job("boom", _ => throw new InvalidOperationException("boom"));
        var loop = new FoundationWorkerLoop(coordinator, [boom, Job("after")], NullLogger<FoundationWorkerLoop>.Instance, Delay);

        await loop.RunAsync(cts.Token);

        delays[0].Should().Be(WorkerSchedulePolicy.GetErrorRetryDelay(), "the first cycle failed");
        coordinator.Ran.Count(n => n == "boom").Should().Be(2, "the loop retried after the error delay");
    }

    [Fact]
    public async Task ANotDueJob_IsSkippedWithoutRunning()
    {
        var ran = false;
        var coordinator = new FakeCoordinator();
        var cts = new CancellationTokenSource();
        Task Delay(TimeSpan _, CancellationToken __) { cts.Cancel(); return Task.CompletedTask; }
        var loop = new FoundationWorkerLoop(
            coordinator,
            [Job("nightly", _ => { ran = true; return Task.CompletedTask; }, due: false)],
            NullLogger<FoundationWorkerLoop>.Instance, Delay);

        await loop.RunAsync(cts.Token);

        ran.Should().BeFalse();
    }

    [Fact]
    public void RejectsAnEmptyJobList()
    {
        var act = () => new FoundationWorkerLoop(new FakeCoordinator(), [], NullLogger<FoundationWorkerLoop>.Instance);

        act.Should().Throw<ArgumentException>();
    }
}
