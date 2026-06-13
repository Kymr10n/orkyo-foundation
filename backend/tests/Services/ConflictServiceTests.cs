using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Api.Tests.Services;

public class ConflictServiceTests
{
    private readonly Mock<IRequestRepository> _requestRepo = new();
    private readonly Mock<IResourceAssignmentValidator> _validator = new();
    private readonly Mock<ICapabilityMatcher> _matcher = new();
    private readonly Mock<IResourceCapabilityRepository> _capRepo = new();
    private readonly ConflictService _service;

    public ConflictServiceTests()
    {
        _validator
            .Setup(v => v.ValidateBatchAsync(It.IsAny<IReadOnlyList<ValidateResourceAssignmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssignmentValidationBatchItem>());
        _capRepo
            .Setup(r => r.GetByResourcesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ResourceCapabilityInfo>());
        // A requirement is satisfied by a resource iff that resource carries any capability (the
        // real matcher's value logic is exercised elsewhere; here we only need the any-resource fan-out).
        _matcher
            .Setup(m => m.Satisfies(It.IsAny<IReadOnlyList<ResourceCapabilityInfo>>(), It.IsAny<RequestRequirementInfo>()))
            .Returns((IReadOnlyList<ResourceCapabilityInfo> caps, RequestRequirementInfo _) => caps.Count > 0);

        _service = new ConflictService(_requestRepo.Object, _validator.Object, _matcher.Object, _capRepo.Object);
    }

    private static readonly DateTime Start = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

    private static ResourceAssignmentInfo Assignment(Guid id, Guid requestId, Guid resourceId, string typeKey, DateTime start, DateTime end) => new()
    {
        Id = id,
        RequestId = requestId,
        ResourceId = resourceId,
        ResourceTypeKey = typeKey,
        StartUtc = start,
        EndUtc = end,
        AssignmentStatus = AssignmentStatuses.Planned,
        CreatedAt = Start,
        UpdatedAt = Start,
    };

    private static ResourceAssignmentInfo SpaceAssignment(Guid id, Guid requestId, Guid spaceId, DateTime start, DateTime end)
        => Assignment(id, requestId, spaceId, ResourceTypeKeys.Space, start, end);

    private static RequestRequirementInfo Requirement(Guid requestId, Guid criterionId, string name = "Skill") => new()
    {
        Id = Guid.NewGuid(),
        RequestId = requestId,
        CriterionId = criterionId,
        Value = JsonSerializer.SerializeToElement(true),
        Criterion = new CriterionBasicInfo { Id = criterionId, Name = name, DataType = CriterionDataType.Boolean },
    };

    private static ResourceCapabilityInfo Capability(Guid resourceId, Guid criterionId) => new()
    {
        Id = Guid.NewGuid(),
        ResourceId = resourceId,
        CriterionId = criterionId,
        Value = JsonSerializer.SerializeToElement(true),
    };

    private static RequestInfo ScheduledRequest(
        Guid id, IReadOnlyList<ResourceAssignmentInfo> assignments, DateTime start, DateTime end,
        int minMinutes = 60, List<RequestRequirementInfo>? requirements = null) => new()
        {
            Id = id,
            Name = "R",
            PlanningMode = PlanningMode.Leaf,
            Status = RequestStatus.Planned,
            SchedulingSettingsApply = false,
            Assignments = [.. assignments],
            StartTs = start,
            EndTs = end,
            MinimalDurationValue = minMinutes,
            MinimalDurationUnit = DurationUnit.Minutes,
            Requirements = requirements,
            CreatedAt = Start,
            UpdatedAt = Start,
        };

    private static AssignmentValidationBatchItem Batch(Guid requestId, Guid resourceId, params ValidationIssue[] blockers) => new()
    {
        RequestId = requestId,
        ResourceId = resourceId,
        Result = new ValidationResult
        {
            Severity = blockers.Length > 0 ? ValidationSeverity.Blocker : ValidationSeverity.Ok,
            Blockers = [.. blockers],
            Warnings = [],
        },
    };

    [Fact]
    public async Task RequirementUnsatisfiedByAnyResource_YieldsConnectorMismatch()
    {
        var reqId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var space = SpaceAssignment(Guid.NewGuid(), reqId, spaceId, Start, Start.AddHours(2));
        // No resource carries any capability → the requirement is unmet by every assignment.
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, [space], Start, Start.AddHours(2),
                requirements: [Requirement(reqId, criterionId)])]);

        var result = await _service.GetAllAsync();

        var entry = Assert.Single(result);
        Assert.Equal(reqId, entry.RequestId);
        Assert.Contains(entry.Conflicts, c => c.Kind == "connector_mismatch" && c.Severity == "error");
    }

    [Fact]
    public async Task RequirementSatisfiedByAssignedPerson_NoCapabilityConflict()
    {
        var reqId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var space = SpaceAssignment(Guid.NewGuid(), reqId, spaceId, Start, Start.AddHours(2));
        var person = Assignment(Guid.NewGuid(), reqId, personId, ResourceTypeKeys.Person, Start, Start.AddHours(2));
        // The room holds no person-skill, but the assigned person does → request-level match passes.
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, [space, person], Start, Start.AddHours(2),
                requirements: [Requirement(reqId, criterionId)])]);
        _capRepo
            .Setup(r => r.GetByResourcesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Capability(personId, criterionId)]);

        var result = await _service.GetAllAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task MapsExclusiveOverbookToOverlapWithPeerRequest()
    {
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        var s1 = SpaceAssignment(a1, r1, spaceId, Start, Start.AddHours(2));
        var s2 = SpaceAssignment(a2, r2, spaceId, Start, Start.AddHours(2));
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                ScheduledRequest(r1, [s1], Start, Start.AddHours(2)),
                ScheduledRequest(r2, [s2], Start, Start.AddHours(2)),
            ]);
        // r1 overbooks against r2's assignment.
        _validator
            .Setup(v => v.ValidateBatchAsync(It.IsAny<IReadOnlyList<ValidateResourceAssignmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Batch(r1, spaceId, new ValidationIssue
            {
                Code = ValidationReasonCode.AssignmentOverbooked,
                Message = "Resource is already assigned during this time window",
                ResourceId = spaceId,
                ConflictingAssignmentId = a2,
            })]);

        var result = await _service.GetAllAsync();

        var entry = Assert.Single(result, e => e.RequestId == r1);
        var overlap = Assert.Single(entry.Conflicts, c => c.Kind == "overlap");
        Assert.Equal(r2, overlap.PeerRequestId);
    }

    [Fact]
    public async Task SurfacesOverbookForNonSpaceAssignment()
    {
        // A double-booked person (not the room) must now surface — the registry evaluates the whole
        // assignment set, not just the space.
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var s1 = SpaceAssignment(Guid.NewGuid(), r1, spaceId, Start, Start.AddHours(2));
        var s2 = SpaceAssignment(Guid.NewGuid(), r2, Guid.NewGuid(), Start, Start.AddHours(2));
        var person1 = Assignment(p1, r1, personId, ResourceTypeKeys.Person, Start, Start.AddHours(2));
        var person2 = Assignment(p2, r2, personId, ResourceTypeKeys.Person, Start, Start.AddHours(2));
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                ScheduledRequest(r1, [s1, person1], Start, Start.AddHours(2)),
                ScheduledRequest(r2, [s2, person2], Start, Start.AddHours(2)),
            ]);
        _validator
            .Setup(v => v.ValidateBatchAsync(It.IsAny<IReadOnlyList<ValidateResourceAssignmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Batch(r1, personId, new ValidationIssue
            {
                Code = ValidationReasonCode.AssignmentOverbooked,
                Message = "Resource is already assigned during this time window",
                ResourceId = personId,
                ConflictingAssignmentId = p2,
            })]);

        var result = await _service.GetAllAsync();

        var entry = Assert.Single(result, e => e.RequestId == r1);
        var overlap = Assert.Single(entry.Conflicts, c => c.Kind == "overlap");
        Assert.Equal(r2, overlap.PeerRequestId);
    }

    [Fact]
    public async Task SurfacesIntrinsicBelowMinDuration()
    {
        var reqId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var space = SpaceAssignment(Guid.NewGuid(), reqId, spaceId, Start, Start.AddMinutes(30));
        // Scheduled 30 min but minimal duration is 60 → below_min_duration (no validator blockers).
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, [space], Start, Start.AddMinutes(30), minMinutes: 60)]);

        var result = await _service.GetAllAsync();

        var entry = Assert.Single(result);
        Assert.Contains(entry.Conflicts, c => c.Kind == "below_min_duration");
    }

    [Fact]
    public async Task ReturnsEmptyWhenNothingScheduled()
    {
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        Assert.Empty(await _service.GetAllAsync());
    }
}
