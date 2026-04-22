using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantLifecycleTransitionPolicyTests
{
    private static readonly DateTime Now = new(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldSuspend_ReturnsTrue_WhenActiveAndOlderThanSuspendThreshold()
    {
        var result = TenantLifecycleTransitionPolicy.ShouldSuspend(
            TenantStatusConstants.Active,
            Now.AddDays(-(LifecyclePolicyConstants.TenantSuspendAfterDormantDays + 1)),
            Now);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldSuspend_ReturnsFalse_AtSuspendThresholdBoundary()
    {
        var result = TenantLifecycleTransitionPolicy.ShouldSuspend(
            TenantStatusConstants.Active,
            Now.AddDays(-LifecyclePolicyConstants.TenantSuspendAfterDormantDays),
            Now);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldMarkDeleting_ReturnsTrue_WhenSuspendedAndOlderThanPurgeThreshold()
    {
        var result = TenantLifecycleTransitionPolicy.ShouldMarkDeleting(
            TenantStatusConstants.Suspended,
            Now.AddDays(-(LifecyclePolicyConstants.UserPurgeAfterDormantDays + 1)),
            Now);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldMarkDeleting_ReturnsFalse_WhenStatusNotSuspended()
    {
        var result = TenantLifecycleTransitionPolicy.ShouldMarkDeleting(
            TenantStatusConstants.Active,
            Now.AddDays(-(LifecyclePolicyConstants.UserPurgeAfterDormantDays + 10)),
            Now);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldPermanentlyDelete_ReturnsTrue_WhenDeletingAndPastGracePeriod()
    {
        var result = TenantLifecycleTransitionPolicy.ShouldPermanentlyDelete(
            TenantStatusConstants.Deleting,
            Now.AddDays(-(LifecyclePolicyConstants.TenantDeleteGraceDays + 1)),
            Now);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldPermanentlyDelete_ReturnsFalse_AtGracePeriodBoundary()
    {
        var result = TenantLifecycleTransitionPolicy.ShouldPermanentlyDelete(
            TenantStatusConstants.Deleting,
            Now.AddDays(-LifecyclePolicyConstants.TenantDeleteGraceDays),
            Now);

        result.Should().BeFalse();
    }
}
