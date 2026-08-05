using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Api.Tests.Services;

public class CalendarFeedServiceTests
{
    private readonly Mock<IRequestRepository> _requestRepo = new();
    private readonly Mock<IResourceRepository> _resourceRepo = new();
    private readonly CalendarFeedService _service;

    private static readonly DateTime Now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid SiteA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SiteB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public CalendarFeedServiceTests()
    {
        _resourceRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ResourceInfo>());
        _service = new CalendarFeedService(_requestRepo.Object, _resourceRepo.Object);
    }

    private static ResourceAssignmentInfo Assignment(Guid resourceId) => new()
    {
        Id = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        ResourceId = resourceId,
        ResourceTypeKey = ResourceTypeKeys.Space,
        StartUtc = Now,
        EndUtc = Now.AddHours(1),
        AssignmentStatus = AssignmentStatuses.Planned,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static RequestInfo Request(
        string name = "Pack customer orders",
        Guid? siteId = null,
        DateTime? start = null,
        IReadOnlyList<ResourceAssignmentInfo>? assignments = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            PlanningMode = PlanningMode.Leaf,
            Status = RequestStatus.New,
            SchedulingSettingsApply = false,
            Assignments = assignments ?? [],
            TargetResourceTypeKeys = [ResourceTypeKeys.Space],
            SiteId = siteId,
            StartTs = start ?? Now.AddDays(1),
            EndTs = (start ?? Now.AddDays(1)).AddHours(2),
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private void GivenScheduled(params RequestInfo[] requests) =>
        _requestRepo
            .Setup(r => r.GetScheduledAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(requests.ToList());

    [Fact]
    public void GenerateToken_ProducesUrlSafeUnguessableValues()
    {
        var a = _service.GenerateToken();
        var b = _service.GenerateToken();

        a.Should().NotBe(b);
        // Must survive a URL path segment untouched.
        a.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        // 256 bits of entropy, base64url-encoded.
        a.Length.Should().BeGreaterThanOrEqualTo(43);
    }

    [Fact]
    public void HashToken_IsStableAndDoesNotEchoTheToken()
    {
        var token = _service.GenerateToken();

        var hash = _service.HashToken(token);

        hash.Should().Be(_service.HashToken(token));
        hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
        hash.Should().NotContain(token);
    }

    [Fact]
    public async Task GetEventsAsync_RequestsABoundedWindowAroundNow()
    {
        GivenScheduled();

        await _service.GetEventsAsync(null, Now);

        // A client refetches the whole document each poll, so the feed must not
        // grow without bound as a tenant accumulates history.
        _requestRepo.Verify(r => r.GetScheduledAsync(
            It.Is<DateTime>(from => from < Now && from > Now.AddMonths(-6)),
            It.Is<DateTime>(to => to > Now && to < Now.AddMonths(24)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEventsAsync_MapsScheduleAndIdentityOntoTheEvent()
    {
        var request = Request(name: "Inspect outbound quality");
        GivenScheduled(request);

        var events = await _service.GetEventsAsync(null, Now);

        var e = events.Should().ContainSingle().Subject;
        e.Id.Should().Be(request.Id);
        e.Summary.Should().Be("Inspect outbound quality");
        e.StartUtc.Should().Be(request.StartTs!.Value);
        e.EndUtc.Should().Be(request.EndTs!.Value);
    }

    [Fact]
    public async Task GetEventsAsync_NamesAssignedResourcesAsTheLocation()
    {
        var resourceId = Guid.NewGuid();
        GivenScheduled(Request(assignments: [Assignment(resourceId)]));
        _resourceRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResourceInfo
            {
                Id = resourceId,
                ResourceTypeId = Guid.NewGuid(),
                ResourceTypeKey = ResourceTypeKeys.Space,
                Name = "FWF-FAB",
                AllocationMode = "exclusive",
                BaseAvailabilityPercent = 100,
                IsActive = true,
            }]);

        var events = await _service.GetEventsAsync(null, Now);

        events.Single().Location.Should().Be("FWF-FAB");
    }

    [Fact]
    public async Task GetEventsAsync_WithoutSiteScopeReturnsEverythingScheduled()
    {
        GivenScheduled(Request(siteId: SiteA), Request(siteId: SiteB), Request(siteId: null));

        (await _service.GetEventsAsync(null, Now)).Should().HaveCount(3);
    }

    [Fact]
    public async Task GetEventsAsync_ScopedToASiteExcludesOtherSites()
    {
        GivenScheduled(Request(name: "here", siteId: SiteA), Request(name: "elsewhere", siteId: SiteB));

        var events = await _service.GetEventsAsync(SiteA, Now);

        events.Should().ContainSingle().Which.Summary.Should().Be("here");
    }

    [Fact]
    public async Task GetEventsAsync_ScopedToASiteKeepsSiteNeutralWork()
    {
        // Site-neutral work is still work the subscriber is expected to do.
        GivenScheduled(Request(name: "anywhere", siteId: null), Request(name: "elsewhere", siteId: SiteB));

        var events = await _service.GetEventsAsync(SiteA, Now);

        events.Should().ContainSingle().Which.Summary.Should().Be("anywhere");
    }

    [Fact]
    public async Task GetEventsAsync_OrdersByStartSoTheFeedReadsChronologically()
    {
        GivenScheduled(
            Request(name: "later", start: Now.AddDays(5)),
            Request(name: "sooner", start: Now.AddDays(1)));

        var events = await _service.GetEventsAsync(null, Now);

        events.Select(e => e.Summary).Should().ContainInOrder("sooner", "later");
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsNothingWhenNothingIsScheduled()
    {
        GivenScheduled();

        (await _service.GetEventsAsync(null, Now)).Should().BeEmpty();
        // No point asking for resource names when there are no assignments.
        _resourceRepo.Verify(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
