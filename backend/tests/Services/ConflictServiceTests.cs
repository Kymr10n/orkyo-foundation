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
    private readonly ConflictService _service;

    public ConflictServiceTests()
    {
        _validator
            .Setup(v => v.ValidateBatchAsync(It.IsAny<IReadOnlyList<ValidateResourceAssignmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssignmentValidationBatchItem>());
        _service = new ConflictService(_requestRepo.Object, _validator.Object);
    }

    private static readonly DateTime Start = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

    private static ResourceAssignmentInfo SpaceAssignment(Guid id, Guid requestId, Guid spaceId, DateTime start, DateTime end) => new()
    {
        Id = id,
        RequestId = requestId,
        ResourceId = spaceId,
        ResourceTypeKey = ResourceTypeKeys.Space,
        StartUtc = start,
        EndUtc = end,
        AssignmentStatus = AssignmentStatuses.Planned,
        CreatedAt = Start,
        UpdatedAt = Start,
    };

    private static RequestInfo ScheduledRequest(
        Guid id, ResourceAssignmentInfo space, DateTime start, DateTime end,
        int minMinutes = 60, List<RequestRequirementInfo>? requirements = null) => new()
        {
            Id = id,
            Name = "R",
            PlanningMode = PlanningMode.Leaf,
            Status = RequestStatus.Planned,
            SchedulingSettingsApply = false,
            Assignments = [space],
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
    public async Task MapsCapabilityBlockerToConnectorMismatch()
    {
        var reqId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var asgnId = Guid.NewGuid();
        var space = SpaceAssignment(asgnId, reqId, spaceId, Start, Start.AddHours(2));
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, space, Start, Start.AddHours(2))]);
        _validator
            .Setup(v => v.ValidateBatchAsync(It.IsAny<IReadOnlyList<ValidateResourceAssignmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Batch(reqId, spaceId, new ValidationIssue
            {
                Code = ValidationReasonCode.CapabilityMissing,
                Message = "Resource does not satisfy requirement",
                ResourceId = spaceId,
            })]);

        var result = await _service.GetAllAsync();

        var entry = Assert.Single(result);
        Assert.Equal(reqId, entry.RequestId);
        Assert.Contains(entry.Conflicts, c => c.Kind == "connector_mismatch" && c.Severity == "error");
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
                ScheduledRequest(r1, s1, Start, Start.AddHours(2)),
                ScheduledRequest(r2, s2, Start, Start.AddHours(2)),
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
    public async Task SurfacesIntrinsicBelowMinDuration()
    {
        var reqId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var space = SpaceAssignment(Guid.NewGuid(), reqId, spaceId, Start, Start.AddMinutes(30));
        // Scheduled 30 min but minimal duration is 60 → below_min_duration (no validator blockers).
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, space, Start, Start.AddMinutes(30), minMinutes: 60)]);

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
