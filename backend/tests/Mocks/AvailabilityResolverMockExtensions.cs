using Api.Models;
using Api.Services;

namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// Shared defaults for <see cref="IAvailabilityResolver"/> mocks.
/// </summary>
public static class AvailabilityResolverMockExtensions
{
    /// <summary>
    /// Stubs the scheduling-settings lookup as "no site settings", which
    /// <see cref="SchedulingEngine.WorkingMinutesInWindow"/> reads as 24/7 capacity.
    ///
    /// Every utilization and insights test predates working-hours masking and asserts
    /// against wall-clock capacity, so this is the shape that keeps them meaningful —
    /// and it doubles as the regression guard that unconfigured sites still get their
    /// old figures. Tests that exercise the mask stub the method themselves instead.
    /// </summary>
    public static Mock<IAvailabilityResolver> WithNoSchedulingSettings(
        this Mock<IAvailabilityResolver> resolver)
    {
        resolver
            .Setup(r => r.GetSchedulingSettingsForResourcesAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SchedulingSettingsInfo>());
        return resolver;
    }
}
