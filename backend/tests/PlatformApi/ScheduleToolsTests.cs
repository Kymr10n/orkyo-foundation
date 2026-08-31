using Api.Models;
using Api.PlatformApi.Mcp;
using Api.Security;
using Api.Services;
using Api.Validators;
using AwesomeAssertions;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.PlatformApi;

/// <summary>
/// The tool surface an agent actually calls.
///
/// These are thin-wrapper tests, not a second copy of the scheduling suite: each asserts that the
/// tool calls the right service with the right arguments, that it surfaces conflicts rather than
/// swallowing them, and that a write tool refuses a read-only token. The business logic behind
/// them is already covered by RequestService / ResourceAssignmentService / ConflictService tests.
///
/// Assertions read the typed result records directly. v1 returned anonymous objects and had to
/// assert against serialized JSON, which could not catch a renamed member.
/// </summary>
public class ScheduleToolsTests
{
    private readonly Mock<IRequestService> _requests = new();
    private readonly Mock<ISchedulingService> _scheduling = new();
    private readonly Mock<IResourceService> _resources = new();
    private readonly Mock<IResourceAssignmentService> _assignments = new();
    private readonly Mock<IConflictService> _conflicts = new();
    private readonly Mock<ISiteService> _sites = new();

    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid ResourceId = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 6, 1, 17, 0, 0, DateTimeKind.Utc);

    // Real validators, not mocks: they are pure and parameterless, and using them means these
    // tests exercise the same refusal rules the HTTP endpoints enforce.
    private ScheduleTools CreateTools(TenantRole role = TenantRole.Editor) =>
        new(_requests.Object, _scheduling.Object, _resources.Object, _assignments.Object,
            _conflicts.Object, _sites.Object,
            new ScheduleRequestRequestValidator(), new CreateResourceAssignmentRequestValidator(),
            McpToolTestContext.ForRole(role));

    private static RequestInfo Request(string name = "Mill the bracket", bool scheduled = true) => new()
    {
        Id = RequestId,
        Name = name,
        PlanningMode = PlanningMode.Leaf,
        Assignments = [],
        TargetResourceTypeKeys = [],
        MinimalDurationValue = 1,
        MinimalDurationUnit = DurationUnit.Hours,
        Status = RequestStatus.New,
        SchedulingSettingsApply = true,
        StartTs = scheduled ? Start : null,
        EndTs = scheduled ? End : null,
    };

    private static ResourceInfo Resource(string name = "Bench 1", bool active = true) => new()
    {
        Id = ResourceId,
        ResourceTypeId = Guid.NewGuid(),
        ResourceTypeKey = "machine",
        Name = name,
        AllocationMode = "Exclusive",
        BaseAvailabilityPercent = 100,
        IsActive = active,
    };

    private static ResourceAssignmentInfo Assignment() => new()
    {
        Id = Guid.NewGuid(),
        RequestId = RequestId,
        ResourceId = ResourceId,
        ResourceTypeKey = "machine",
        StartUtc = Start,
        EndUtc = End,
        AssignmentStatus = "Planned",
    };

    private static RequestConflictInfo ConflictFor(Guid requestId, string severity = "error") => new()
    {
        RequestId = requestId,
        Conflicts =
        [
            new ConflictInfo
            {
                Id = "c1",
                Kind = "overlap",
                Severity = severity,
                Message = "Bench 1 is double-booked",
                ResourceId = ResourceId,
            },
        ],
    };

    private void SetupSearch(params RequestInfo[] results) =>
        _requests.Setup(s => s.SearchAsync(It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<int>(),
                It.IsAny<RequestSort>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. results]);

    private void SetupResources(params ResourceInfo[] results) =>
        _resources.Setup(s => s.GetAllAsync(It.IsAny<ResourceListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. results]);

    private void SetupConflicts(params RequestConflictInfo[] results) =>
        _conflicts.Setup(s => s.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync([.. results]);

    // ── list_sites ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListSites_ReturnsTheSitesAutoScheduleNeeds()
    {
        // auto_schedule_preview requires a siteId and nothing else in the tool surface produces
        // one, so this tool is the entry point for the whole scale story.
        var siteId = Guid.NewGuid();
        _sites.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SiteInfo { Id = siteId, Name = "Bristol", Code = "BRS" }]);

        var result = await CreateTools().ListSitesAsync();

        result.Should().ContainSingle();
        result[0].Id.Should().Be(siteId);
        result[0].Name.Should().Be("Bristol");
    }

    [Fact]
    public async Task ListSites_IsAllowedForAReadOnlyToken()
    {
        _sites.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        (await CreateTools(TenantRole.Viewer).ListSitesAsync()).Should().BeEmpty();
    }

    // ── list_requests ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListRequests_PassesTheFiltersThroughToTheSearch()
    {
        SetupSearch(Request());

        await CreateTools().ListRequestsAsync(scheduled: false, nameContains: "mill", limit: 25);

        _requests.Verify(s => s.SearchAsync("mill", false, 25, It.IsAny<RequestSort>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, 200)]
    public async Task ListRequests_ClampsTheLimitSoAnAgentCannotAskForTheWholeTenant(int asked, int expected)
    {
        SetupSearch();

        await CreateTools().ListRequestsAsync(limit: asked);

        _requests.Verify(s => s.SearchAsync(It.IsAny<string?>(), It.IsAny<bool?>(), expected,
            It.IsAny<RequestSort>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListRequests_TellsTheModelWhetherEachRequestIsScheduled()
    {
        SetupSearch(Request("Scheduled one"), Request("Backlog one", scheduled: false));

        var result = await CreateTools().ListRequestsAsync();

        result.Count.Should().Be(2);
        result.Requests.Single(r => r.Name == "Scheduled one").IsScheduled.Should().BeTrue();
        result.Requests.Single(r => r.Name == "Backlog one").IsScheduled.Should().BeFalse();
    }

    [Fact]
    public async Task ListRequests_IsAllowedForAReadOnlyToken()
    {
        SetupSearch(Request());

        var result = await CreateTools(TenantRole.Viewer).ListRequestsAsync();

        result.Requests.Should().ContainSingle(r => r.Name == "Mill the bracket");
    }

    // ── list_resources ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListResources_DefaultsToActiveResourcesOnly()
    {
        // An agent offered a deactivated machine would book work onto something out of service.
        SetupResources(Resource());

        await CreateTools().ListResourcesAsync();

        _resources.Verify(s => s.GetAllAsync(
            It.Is<ResourceListFilter>(f => f.IsActive == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListResources_CanIncludeInactiveWhenAsked()
    {
        SetupResources(Resource(active: false));

        await CreateTools().ListResourcesAsync(includeInactive: true);

        _resources.Verify(s => s.GetAllAsync(
            It.Is<ResourceListFilter>(f => f.IsActive == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListResources_PassesTheTypeAndSearchFilters()
    {
        SetupResources();

        await CreateTools().ListResourcesAsync(resourceTypeKey: "machine", search: "bench");

        _resources.Verify(s => s.GetAllAsync(
            It.Is<ResourceListFilter>(f => f.ResourceTypeKey == "machine" && f.Search == "bench"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListResources_ReturnsTheIdAndTypeAnAssignmentNeeds()
    {
        SetupResources(Resource());

        var result = await CreateTools().ListResourcesAsync();

        var only = result.Resources.Should().ContainSingle().Subject;
        only.Id.Should().Be(ResourceId);
        only.ResourceTypeKey.Should().Be("machine");
        only.Name.Should().Be("Bench 1");
    }

    // ── list_conflicts ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListConflicts_PassesTheWindowThrough()
    {
        SetupConflicts();

        await CreateTools().ListConflictsAsync(Start, End);

        _conflicts.Verify(s => s.GetAllAsync(Start, End, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListConflicts_ReportsKindSeverityAndTheResourceInvolved()
    {
        SetupConflicts(ConflictFor(RequestId));

        var result = await CreateTools().ListConflictsAsync();

        result.Count.Should().Be(1);
        var detail = result.Conflicts.Single().Conflicts.Single();
        detail.Kind.Should().Be("overlap");
        detail.Severity.Should().Be("error");
        detail.Message.Should().Contain("double-booked");
        detail.ResourceId.Should().Be(ResourceId);
    }

    [Fact]
    public async Task ListConflicts_IsAllowedForAReadOnlyToken()
    {
        SetupConflicts(ConflictFor(RequestId));

        var result = await CreateTools(TenantRole.Viewer).ListConflictsAsync();

        result.Conflicts.Should().ContainSingle();
    }

    // ── reschedule_request ───────────────────────────────────────────────────

    [Fact]
    public async Task Reschedule_NormalisesThroughSchedulingBeforeWriting()
    {
        // Both calls, in this order — the same pair the HTTP endpoint makes. Skipping the
        // scheduling service would let a tool produce a placement the UI could not.
        var adjusted = new ScheduleRequestRequest { ResourceId = ResourceId, StartTs = Start, EndTs = End };
        _scheduling.Setup(s => s.ApplySchedulingToScheduleAsync(RequestId,
            It.IsAny<ScheduleRequestRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(adjusted);
        _requests.Setup(s => s.UpdateScheduleAsync(RequestId, adjusted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Request());
        SetupConflicts();

        await CreateTools().RescheduleRequestAsync(RequestId, Start, End, ResourceId);

        _scheduling.Verify(s => s.ApplySchedulingToScheduleAsync(RequestId,
            It.Is<ScheduleRequestRequest>(r => r.ResourceId == ResourceId && r.StartTs == Start),
            It.IsAny<CancellationToken>()), Times.Once);
        _requests.Verify(s => s.UpdateScheduleAsync(RequestId, adjusted, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reschedule_ReportsAConflictTheMoveCreated()
    {
        // A move that succeeds can still be a move that overbooks. Reporting "done" alone would
        // let an agent believe a broken schedule is a good one.
        _scheduling.Setup(s => s.ApplySchedulingToScheduleAsync(It.IsAny<Guid>(),
            It.IsAny<ScheduleRequestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleRequestRequest());
        _requests.Setup(s => s.UpdateScheduleAsync(It.IsAny<Guid>(), It.IsAny<ScheduleRequestRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(Request());
        SetupConflicts(ConflictFor(RequestId));

        var result = await CreateTools().RescheduleRequestAsync(RequestId, Start, End, ResourceId);

        result.Conflicts.Should().ContainSingle(c => c.Message.Contains("double-booked"));
    }

    [Fact]
    public async Task Reschedule_DoesNotReportAnotherRequestsConflicts()
    {
        _scheduling.Setup(s => s.ApplySchedulingToScheduleAsync(It.IsAny<Guid>(),
            It.IsAny<ScheduleRequestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleRequestRequest());
        _requests.Setup(s => s.UpdateScheduleAsync(It.IsAny<Guid>(), It.IsAny<ScheduleRequestRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(Request());
        SetupConflicts(ConflictFor(Guid.NewGuid()));

        var result = await CreateTools().RescheduleRequestAsync(RequestId, Start, End, ResourceId);

        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task Reschedule_SaysSoWhenTheRequestDoesNotExist()
    {
        _scheduling.Setup(s => s.ApplySchedulingToScheduleAsync(It.IsAny<Guid>(),
            It.IsAny<ScheduleRequestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleRequestRequest());
        _requests.Setup(s => s.UpdateScheduleAsync(It.IsAny<Guid>(), It.IsAny<ScheduleRequestRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((RequestInfo?)null);

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().RescheduleRequestAsync(RequestId, Start, End, ResourceId));

        thrown.Message.Should().Contain(RequestId.ToString());
    }

    [Fact]
    public async Task Reschedule_IsRefusedForAReadOnlyToken()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools(TenantRole.Viewer).RescheduleRequestAsync(RequestId, Start, End));

        thrown.Message.Should().Contain(PlatformApiScopes.ScheduleWrite);
        // Refused before anything is touched — not attempted and rolled back.
        _scheduling.VerifyNoOtherCalls();
        _requests.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reschedule_RefusesAPartialScheduleBeforeTouchingAnything()
    {
        // The HTTP contract is all-three-or-nothing; a lone start with no end (or no resource)
        // used to slip past the tool and persist shapes the endpoint refuses.
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().RescheduleRequestAsync(RequestId, Start));

        thrown.Message.Should().Contain("To schedule, provide resourceId, startTs, and endTs");
        _scheduling.VerifyNoOtherCalls();
        _requests.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reschedule_RefusesEndBeforeStart()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().RescheduleRequestAsync(RequestId, End, Start, ResourceId));

        thrown.Message.Should().Contain("End time must be after start time");
        _requests.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reschedule_UnschedulingWithAllNulls_IsStillAllowed()
    {
        _scheduling.Setup(s => s.ApplySchedulingToScheduleAsync(RequestId,
            It.IsAny<ScheduleRequestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleRequestRequest());
        _requests.Setup(s => s.UpdateScheduleAsync(RequestId, It.IsAny<ScheduleRequestRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(Request(scheduled: false));
        SetupConflicts();

        var result = await CreateTools().RescheduleRequestAsync(RequestId);

        result.Request.IsScheduled.Should().BeFalse();
    }

    // ── assign_resource ──────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_PassesTheBookingThroughToTheAssignmentService()
    {
        _assignments.Setup(s => s.CreateAsync(It.IsAny<CreateResourceAssignmentRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((Assignment(), null));

        await CreateTools().AssignResourceAsync(RequestId, ResourceId, Start, End, allocationPercent: 50);

        _assignments.Verify(s => s.CreateAsync(It.Is<CreateResourceAssignmentRequest>(r =>
            r.RequestId == RequestId && r.ResourceId == ResourceId
            && r.StartUtc == Start && r.EndUtc == End && r.AllocationPercent == 50),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_ReportsSuccessWithTheBookingItMade()
    {
        _assignments.Setup(s => s.CreateAsync(It.IsAny<CreateResourceAssignmentRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((Assignment(), null));

        var result = await CreateTools().AssignResourceAsync(RequestId, ResourceId, Start, End);

        result.Assigned.Should().BeTrue();
        result.Assignment!.ResourceId.Should().Be(ResourceId);
        result.Conflict.Should().BeNull();
    }

    [Fact]
    public async Task Assign_ReturnsABlockingConflictAsAnAnswer_NotAnError()
    {
        // The agent is expected to read this and pick another resource or window, so a refusal
        // must come back as data rather than as a thrown protocol error.
        var conflict = new ResourceConflict
        {
            ResourceId = ResourceId,
            Type = ResourceConflictType.ExclusiveOverlap,
            Message = "Already booked for that window",
        };
        _assignments.Setup(s => s.CreateAsync(It.IsAny<CreateResourceAssignmentRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((null, conflict));

        var result = await CreateTools().AssignResourceAsync(RequestId, ResourceId, Start, End);

        result.Assigned.Should().BeFalse();
        result.Assignment.Should().BeNull();
        result.Conflict!.Type.Should().Be(nameof(ResourceConflictType.ExclusiveOverlap));
        result.Conflict.Message.Should().Contain("Already booked");
    }

    [Fact]
    public async Task Assign_SurfacesASoftConflictAlongsideASuccessfulBooking()
    {
        // Soft constraints do not block a manual assignment, but hiding them would leave the agent
        // thinking the booking was clean.
        var soft = new ResourceConflict
        {
            ResourceId = ResourceId,
            Type = ResourceConflictType.SiteMismatch,
            Message = "Resource is normally at another site",
        };
        _assignments.Setup(s => s.CreateAsync(It.IsAny<CreateResourceAssignmentRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((Assignment(), soft));

        var result = await CreateTools().AssignResourceAsync(RequestId, ResourceId, Start, End);

        result.Assigned.Should().BeTrue();
        result.Conflict!.Type.Should().Be(nameof(ResourceConflictType.SiteMismatch));
    }

    [Fact]
    public async Task Assign_RefusesAZeroLengthWindowBeforeWriting()
    {
        // The validator's own docstring is the reason: a zero-length window "silently matches
        // nothing in the overlap queries" — a booking invisible to conflict detection.
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().AssignResourceAsync(RequestId, ResourceId, Start, Start));

        thrown.Message.Should().Contain("EndUtc must be after StartUtc");
        _assignments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Assign_RefusesAnOutOfRangeAllocation()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(() =>
            CreateTools().AssignResourceAsync(RequestId, ResourceId, Start, End, allocationPercent: 5000));

        _assignments.VerifyNoOtherCalls();
        thrown.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Assign_IsRefusedForAReadOnlyToken()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools(TenantRole.Viewer).AssignResourceAsync(RequestId, ResourceId, Start, End));

        thrown.Message.Should().Contain(PlatformApiScopes.ScheduleWrite);
        _assignments.VerifyNoOtherCalls();
    }
}
