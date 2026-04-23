using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Shared;

public class WorkerSchedulePolicyTests
{
    private static readonly DateTime Now = new(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldRunTenantLifecycle_ReturnsTrue_WhenLastRunIsMinValue()
    {
        var result = WorkerSchedulePolicy.ShouldRunTenantLifecycle(Now, DateTime.MinValue);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunTenantLifecycle_ReturnsFalse_AtExactIntervalBoundary()
    {
        var result = WorkerSchedulePolicy.ShouldRunTenantLifecycle(
            Now,
            Now - TimePolicyConstants.WorkerTenantLifecycleInterval);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRunTenantLifecycle_ReturnsTrue_WhenPastInterval()
    {
        var result = WorkerSchedulePolicy.ShouldRunTenantLifecycle(
            Now,
            Now - TimePolicyConstants.WorkerTenantLifecycleInterval - TimeSpan.FromSeconds(1));

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunUserLifecycle_ReturnsFalse_AtExactIntervalBoundary()
    {
        var result = WorkerSchedulePolicy.ShouldRunUserLifecycle(
            Now,
            Now - TimePolicyConstants.WorkerUserLifecycleInterval);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRunUserLifecycle_ReturnsTrue_WhenPastInterval()
    {
        var result = WorkerSchedulePolicy.ShouldRunUserLifecycle(
            Now,
            Now - TimePolicyConstants.WorkerUserLifecycleInterval - TimeSpan.FromSeconds(1));

        result.Should().BeTrue();
    }

    [Fact]
    public void GetLoopDelay_ReturnsBaseDelay_WhenNoJitter()
    {
        var result = WorkerSchedulePolicy.GetLoopDelay(TimeSpan.Zero);

        result.Should().Be(TimePolicyConstants.WorkerLoopDelay);
    }

    [Fact]
    public void GetLoopDelay_AddsJitterToBaseDelay()
    {
        var jitter = TimeSpan.FromSeconds(12);

        var result = WorkerSchedulePolicy.GetLoopDelay(jitter);

        result.Should().Be(TimePolicyConstants.WorkerLoopDelay + jitter);
    }

    [Fact]
    public void GetErrorRetryDelay_ReturnsConfiguredRetryDelay()
    {
        var result = WorkerSchedulePolicy.GetErrorRetryDelay();

        result.Should().Be(TimePolicyConstants.WorkerErrorRetryDelay);
    }
}
