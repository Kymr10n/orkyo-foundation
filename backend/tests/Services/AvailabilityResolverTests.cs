using Api.Models;
using Api.Repositories;
using Api.Services;

namespace Api.Tests.Services;

public class AvailabilityResolverTests
{
    private readonly Mock<IAvailabilityEventRepository> _eventRepo = new();
    private readonly Mock<IResourceAbsenceRepository> _absenceRepo = new();
    private readonly Mock<ISchedulingRepository> _schedulingRepo = new();
    private readonly Mock<IResourceGroupMemberRepository> _groupRepo = new();

    public AvailabilityResolverTests()
    {
        _absenceRepo
            .Setup(r => r.GetByResourceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ResourceAbsenceInfo>());
        _absenceRepo
            .Setup(r => r.GetEnabledByResourcesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, List<ResourceAbsenceInfo>>());
        _eventRepo
            .Setup(r => r.GetEnabledBySiteWithScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AvailabilityEventInfo>());
        _groupRepo
            .Setup(r => r.GetGroupIdsForResourceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        _groupRepo
            .Setup(r => r.GetGroupIdsForResourcesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Guid>>());
        _schedulingRepo
            .Setup(r => r.GetResourceTypeIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());
        _schedulingRepo
            .Setup(r => r.GetSiteIdsForResourcesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());
    }

    [Fact]
    public async Task GetBlockedPeriodsAsync_AppliesResourceTypeClosedScope()
    {
        var siteId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var ev = CreateEvent(DefaultEffect.Available, [
            CreateScope(ScopeTargetType.ResourceType, typeId, ScopeEffect.Closed),
        ]);

        _schedulingRepo.Setup(r => r.GetSiteIdsForResourcesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [resourceId] = siteId });
        _schedulingRepo.Setup(r => r.GetResourceTypeIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { resourceId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [resourceId] = typeId });
        _eventRepo.Setup(r => r.GetEnabledBySiteWithScopesAsync(siteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ev]);

        var resolver = CreateResolver();

        var blocked = await resolver.GetBlockedPeriodsAsync(resourceId);

        blocked.Should().ContainSingle(p =>
            p.Id == ev.Id && p.Source == BlockedPeriodSource.AvailabilityEvent);
    }

    [Fact]
    public async Task GetBlockedPeriodsForResourcesAsync_UsesResourceTypeScopesPerResource()
    {
        var siteId = Guid.NewGuid();
        var matchingResourceId = Guid.NewGuid();
        var otherResourceId = Guid.NewGuid();
        var matchingTypeId = Guid.NewGuid();
        var otherTypeId = Guid.NewGuid();
        var ev = CreateEvent(DefaultEffect.Available, [
            CreateScope(ScopeTargetType.ResourceType, matchingTypeId, ScopeEffect.Closed),
        ]);

        _schedulingRepo.Setup(r => r.GetResourceTypeIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(matchingResourceId) && ids.Contains(otherResourceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>
            {
                [matchingResourceId] = matchingTypeId,
                [otherResourceId] = otherTypeId,
            });
        _eventRepo.Setup(r => r.GetEnabledBySiteWithScopesAsync(siteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ev]);

        var resolver = CreateResolver();

        var blocked = await resolver.GetBlockedPeriodsForResourcesAsync(
            siteId, [matchingResourceId, otherResourceId]);

        blocked[matchingResourceId].Should().ContainSingle(p => p.Id == ev.Id);
        blocked[otherResourceId].Should().BeEmpty();
    }

    [Fact]
    public async Task GetBlockedPeriodsAsync_ResourceOverrideWinsOverResourceTypeScope()
    {
        var siteId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var ev = CreateEvent(DefaultEffect.Available, [
            CreateScope(ScopeTargetType.ResourceType, typeId, ScopeEffect.Closed),
            CreateScope(ScopeTargetType.Resource, resourceId, ScopeEffect.Available),
        ]);

        _schedulingRepo.Setup(r => r.GetSiteIdsForResourcesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [resourceId] = siteId });
        _schedulingRepo.Setup(r => r.GetResourceTypeIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [resourceId] = typeId });
        _eventRepo.Setup(r => r.GetEnabledBySiteWithScopesAsync(siteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ev]);

        var resolver = CreateResolver();

        var blocked = await resolver.GetBlockedPeriodsAsync(resourceId);

        blocked.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBlockedPeriodsAsync_PublicHoliday_ExcludedWhenHolidaysDisabled()
    {
        // A closing public-holiday event must NOT block when the site keeps holidays off.
        var siteId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var ev = CreateEvent(DefaultEffect.Closed, [], AvailabilityEventType.PublicHoliday);

        _schedulingRepo.Setup(r => r.GetSiteIdsForResourcesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [resourceId] = siteId });
        _eventRepo.Setup(r => r.GetEnabledBySiteWithScopesAsync(siteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ev]);
        _schedulingRepo.Setup(r => r.GetSettingsAsync(siteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SchedulingSettingsInfo.Default(siteId) with { PublicHolidaysEnabled = false });

        var blocked = await CreateResolver().GetBlockedPeriodsAsync(resourceId);

        blocked.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBlockedPeriodsAsync_PublicHoliday_IncludedWhenHolidaysEnabled()
    {
        var siteId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var ev = CreateEvent(DefaultEffect.Closed, [], AvailabilityEventType.PublicHoliday);

        _schedulingRepo.Setup(r => r.GetSiteIdsForResourcesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [resourceId] = siteId });
        _eventRepo.Setup(r => r.GetEnabledBySiteWithScopesAsync(siteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ev]);
        _schedulingRepo.Setup(r => r.GetSettingsAsync(siteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SchedulingSettingsInfo.Default(siteId) with { PublicHolidaysEnabled = true });

        var blocked = await CreateResolver().GetBlockedPeriodsAsync(resourceId);

        blocked.Should().ContainSingle(p => p.Id == ev.Id);
    }

    private AvailabilityResolver CreateResolver() => new(
        _eventRepo.Object,
        _absenceRepo.Object,
        _schedulingRepo.Object,
        _groupRepo.Object);

    private static AvailabilityEventInfo CreateEvent(
        DefaultEffect defaultEffect,
        List<AvailabilityEventScopeInfo> scopes,
        AvailabilityEventType eventType = AvailabilityEventType.Custom) => new()
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Title = "Availability event",
            EventType = eventType,
            DefaultEffect = defaultEffect,
            StartTs = new DateTime(2026, 12, 24, 0, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc),
            IsRecurring = false,
            Enabled = true,
            Scopes = scopes,
        };

    private static AvailabilityEventScopeInfo CreateScope(
        ScopeTargetType targetType,
        Guid targetId,
        ScopeEffect effect) => new()
        {
            Id = Guid.NewGuid(),
            AvailabilityEventId = Guid.NewGuid(),
            TargetType = targetType,
            TargetId = targetId,
            Effect = effect,
        };
}
