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
    private readonly Mock<IRequestDependencyRepository> _dependencyRepo = new();
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

        // No precedence edges unless a test adds them; the dependency tests below install their own.
        // GetByIdsAsync resolves predecessors that sit outside the scheduled batch — it is reached
        // whenever an edge points off-batch, so it needs a default even for tests that add no edges.
        _requestRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dependencyRepo
            .Setup(r => r.GetBySuccessorsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _service = new ConflictService(
            _requestRepo.Object, _validator.Object, _matcher.Object, _capRepo.Object, _dependencyRepo.Object);
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

    /// <summary>A request that exists but has no dates yet — it can still be placed.</summary>
    private static RequestInfo Unscheduled(Guid id) => new()
    {
        Id = id,
        Name = "R",
        PlanningMode = PlanningMode.Leaf,
        Status = RequestStatus.New,
        SchedulingSettingsApply = false,
        Assignments = [],
        TargetResourceTypeKeys = [ResourceTypeKeys.Space],
        MinimalDurationValue = 60,
        MinimalDurationUnit = DurationUnit.Minutes,
        CreatedAt = Start,
        UpdatedAt = Start,
    };

    private static RequestInfo ScheduledRequest(
        Guid id, IReadOnlyList<ResourceAssignmentInfo> assignments, DateTime start, DateTime end,
        int minMinutes = 60, List<RequestRequirementInfo>? requirements = null,
        DateTime? earliestStart = null, DateTime? latestEnd = null,
        PredecessorLogic logic = PredecessorLogic.All, int? k = null,
        RequestStatus status = RequestStatus.New) => new()
        {
            Id = id,
            Name = "R",
            PlanningMode = PlanningMode.Leaf,
            PredecessorLogic = logic,
            PredecessorLogicK = k,
            Status = status,
            SchedulingSettingsApply = false,
            Assignments = [.. assignments],
            TargetResourceTypeKeys = [ResourceTypeKeys.Space],
            StartTs = start,
            EndTs = end,
            MinimalDurationValue = minMinutes,
            MinimalDurationUnit = DurationUnit.Minutes,
            EarliestStartTs = earliestStart,
            LatestEndTs = latestEnd,
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
        // Carries the unmet criterion so the editor can flag that requirement row.
        Assert.Contains(entry.Conflicts, c => c.Kind == "connector_mismatch" && c.Severity == "error" && c.CriterionId == criterionId);
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
    public async Task TwoOffTimePeriodsOnOneAssignmentGetDistinctConflictIds()
    {
        // One assignment can overlap several blocked periods — two closures, or a holiday
        // and a shutdown — and the validator raises one issue per period, all with the same
        // code and resource. The conflict id has to tell them apart: React keys on it, and
        // duplicates meant one of the two conflicts was silently dropped from the list.
        var requestId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var assignment = SpaceAssignment(Guid.NewGuid(), requestId, spaceId, Start, Start.AddHours(8));
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(requestId, [assignment], Start, Start.AddHours(8))]);

        _validator
            .Setup(v => v.ValidateBatchAsync(It.IsAny<IReadOnlyList<ValidateResourceAssignmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Batch(requestId, spaceId,
                new ValidationIssue
                {
                    Code = ValidationReasonCode.OffTimeOverlap,
                    Message = "Resource has off-time during this period",
                    ResourceId = spaceId,
                    ConflictingAvailabilityId = Guid.NewGuid(),
                },
                new ValidationIssue
                {
                    Code = ValidationReasonCode.OffTimeOverlap,
                    Message = "Resource has off-time during this period",
                    ResourceId = spaceId,
                    ConflictingAvailabilityId = Guid.NewGuid(),
                })]);

        var result = await _service.GetAllAsync();

        var entry = Assert.Single(result, e => e.RequestId == requestId);
        var offTime = entry.Conflicts.Where(c => c.Id.EndsWith("-offtime", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, offTime.Count);
        Assert.Equal(2, offTime.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public async Task ReportsAnAbsenceAsAnErrorRatherThanAnOffTimeWarning()
    {
        // A site closure says the hours are unusual; an absence says the resource is gone. The
        // second one makes the booking wrong, so it must not read as the same soft warning —
        // a machine blocked for maintenance under a booked job was showing amber, like a weekend.
        var requestId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        var assignment = SpaceAssignment(Guid.NewGuid(), requestId, machineId, Start, Start.AddHours(8));
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(requestId, [assignment], Start, Start.AddHours(8))]);

        _validator
            .Setup(v => v.ValidateBatchAsync(It.IsAny<IReadOnlyList<ValidateResourceAssignmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Batch(requestId, machineId,
                new ValidationIssue
                {
                    Code = ValidationReasonCode.ResourceAbsence,
                    Message = "Resource is unavailable during this period (Maintenance)",
                    ResourceId = machineId,
                    ConflictingAvailabilityId = Guid.NewGuid(),
                })]);

        var result = await _service.GetAllAsync();

        var entry = Assert.Single(result, e => e.RequestId == requestId);
        var conflict = Assert.Single(entry.Conflicts, c => c.Kind == ConflictKinds.ResourceUnavailable);
        Assert.Equal(ConflictSeverities.Error, conflict.Severity);
        Assert.Equal(machineId, conflict.ResourceId);
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
        Assert.Equal(personId, overlap.ResourceId); // the double-booked person, so the editor flags that row
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
    public async Task SurfacesIntrinsicBeforeEarliestStart()
    {
        var reqId = Guid.NewGuid();
        var space = SpaceAssignment(Guid.NewGuid(), reqId, Guid.NewGuid(), Start, Start.AddHours(1));
        // Scheduled an hour before the window the request itself declares.
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, [space], Start, Start.AddHours(1),
                minMinutes: 30, earliestStart: Start.AddHours(1))]);

        var result = await _service.GetAllAsync();

        var conflict = Assert.Single(Assert.Single(result).Conflicts,
            c => c.Kind == "before_earliest_start");
        Assert.Equal("error", conflict.Severity);
    }

    [Fact]
    public async Task SurfacesIntrinsicAfterLatestEnd()
    {
        var reqId = Guid.NewGuid();
        var space = SpaceAssignment(Guid.NewGuid(), reqId, Guid.NewGuid(), Start, Start.AddHours(2));
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, [space], Start, Start.AddHours(2),
                minMinutes: 30, latestEnd: Start.AddHours(1))]);

        var result = await _service.GetAllAsync();

        var conflict = Assert.Single(Assert.Single(result).Conflicts,
            c => c.Kind == "after_latest_end");
        Assert.Equal("error", conflict.Severity);
    }

    [Fact]
    public async Task ReturnsEmptyWhenNothingScheduled()
    {
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        Assert.Empty(await _service.GetAllAsync());
    }

    // Regression guard: ConflictInfo.Id must be unique per resource so the frontend can use it as a
    // React key without hitting duplicate-key warnings.

    [Fact]
    public async Task TwoResourcesOverCapacity_YieldDistinctCapacityExceededIds()
    {
        var reqId = Guid.NewGuid();
        var person1 = Guid.NewGuid();
        var person2 = Guid.NewGuid();
        var a1 = Assignment(Guid.NewGuid(), reqId, person1, ResourceTypeKeys.Person, Start, Start.AddHours(2));
        var a2 = Assignment(Guid.NewGuid(), reqId, person2, ResourceTypeKeys.Person, Start, Start.AddHours(2));
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, [a1, a2], Start, Start.AddHours(2))]);
        // Two Fractional over-capacity issues: no ConflictingAssignmentId → capacity_exceeded path.
        _validator
            .Setup(v => v.ValidateBatchAsync(It.IsAny<IReadOnlyList<ValidateResourceAssignmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Batch(reqId, person1, new ValidationIssue { Code = ValidationReasonCode.AssignmentOverbooked, Message = "over", ResourceId = person1 }),
                Batch(reqId, person2, new ValidationIssue { Code = ValidationReasonCode.AssignmentOverbooked, Message = "over", ResourceId = person2 }),
            ]);

        var result = await _service.GetAllAsync();

        var entry = Assert.Single(result);
        var capacityConflicts = entry.Conflicts.Where(c => c.Kind == "capacity_exceeded").ToList();
        Assert.Equal(2, capacityConflicts.Count);
        Assert.Equal(2, capacityConflicts.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public async Task TwoResourcesWithSameOffTimeCode_YieldDistinctStartsInOffTimeIds()
    {
        var reqId = Guid.NewGuid();
        var person1 = Guid.NewGuid();
        var person2 = Guid.NewGuid();
        var a1 = Assignment(Guid.NewGuid(), reqId, person1, ResourceTypeKeys.Person, Start, Start.AddHours(2));
        var a2 = Assignment(Guid.NewGuid(), reqId, person2, ResourceTypeKeys.Person, Start, Start.AddHours(2));
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, [a1, a2], Start, Start.AddHours(2))]);
        _validator
            .Setup(v => v.ValidateBatchAsync(It.IsAny<IReadOnlyList<ValidateResourceAssignmentRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                BatchWarn(reqId, person1, new ValidationIssue { Code = ValidationReasonCode.OffTimeOverlap, Message = "off", ResourceId = person1 }),
                BatchWarn(reqId, person2, new ValidationIssue { Code = ValidationReasonCode.OffTimeOverlap, Message = "off", ResourceId = person2 }),
            ]);

        var result = await _service.GetAllAsync();

        var entry = Assert.Single(result);
        var offTimeConflicts = entry.Conflicts.Where(c => c.Kind == "starts_in_off_time").ToList();
        Assert.Equal(2, offTimeConflicts.Count);
        Assert.Equal(2, offTimeConflicts.Select(c => c.Id).Distinct().Count());
        // Each conflict carries the resource it's about, so the editor can flag that person row.
        Assert.Contains(offTimeConflicts, c => c.ResourceId == person1);
        Assert.Contains(offTimeConflicts, c => c.ResourceId == person2);
    }

    [Fact]
    public async Task GetAllAsync_WithWindow_QueriesWindowedScheduledRequests()
    {
        var from = Start;
        var to = Start.AddDays(7);
        var reqId = Guid.NewGuid();
        var space = SpaceAssignment(Guid.NewGuid(), reqId, Guid.NewGuid(), Start, Start.AddMinutes(10));
        // 10-minute bar against a 60-minute minimum → a deterministic intrinsic conflict, no validator setup.
        _requestRepo.Setup(r => r.GetScheduledAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(reqId, [space], Start, Start.AddMinutes(10), minMinutes: 60)]);

        var result = await _service.GetAllAsync(from, to);

        var entry = Assert.Single(result);
        Assert.Equal(reqId, entry.RequestId);
        Assert.Contains(entry.Conflicts, c => c.Kind == "below_min_duration");
        _requestRepo.Verify(r => r.GetScheduledAsync(from, to, It.IsAny<CancellationToken>()), Times.Once);
        _requestRepo.Verify(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAllAsync_WithoutWindow_QueriesAllTimeScheduledRequests()
    {
        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.GetAllAsync();

        Assert.Empty(result);
        _requestRepo.Verify(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()), Times.Once);
        _requestRepo.Verify(
            r => r.GetScheduledAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Precedence (dependency) conflicts ──────────────────────────────────────

    private static RequestDependencyInfo Edge(Guid predecessorId, Guid successorId, int lagMinutes = 0) => new()
    {
        Id = Guid.NewGuid(),
        PredecessorRequestId = predecessorId,
        SuccessorRequestId = successorId,
        PredecessorName = "Cut steel",
        SuccessorName = "Weld frame",
        DependencyType = DependencyTypes.FinishToStart,
        LagMinutes = lagMinutes,
        CreatedAt = Start,
    };

    /// <summary>
    /// Schedules a predecessor ending on <paramref name="predecessorEnd"/> and a successor starting
    /// on <paramref name="successorStart"/>, linked by one edge, and returns the successor's conflicts.
    /// </summary>
    private async Task<IReadOnlyList<ConflictInfo>> DependencyConflictsAsync(
        DateTime predecessorEnd, DateTime successorStart, int lagMinutes = 0)
    {
        var predecessorId = Guid.NewGuid();
        var successorId = Guid.NewGuid();

        var predecessor = ScheduledRequest(
            predecessorId, [], predecessorEnd.AddHours(-1), predecessorEnd);
        var successor = ScheduledRequest(
            successorId, [], successorStart, successorStart.AddHours(1));

        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([predecessor, successor]);
        _dependencyRepo
            .Setup(r => r.GetBySuccessorsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Edge(predecessorId, successorId, lagMinutes)]);

        var result = await _service.GetAllAsync();
        var entry = result.SingleOrDefault(e => e.RequestId == successorId);
        return entry?.Conflicts ?? [];
    }

    [Fact]
    public async Task ASuccessorStartingBeforeItsPredecessorFinishesIsAnError()
    {
        var conflicts = await DependencyConflictsAsync(
            predecessorEnd: Start.AddDays(2), successorStart: Start.AddDays(1));

        var conflict = Assert.Single(conflicts, c => c.Kind == ConflictKinds.DependencyViolation);
        Assert.Equal(ConflictSeverities.Error, conflict.Severity);
        Assert.Contains("Cut steel", conflict.Message);
        Assert.DoesNotContain("gap", conflict.Message);
    }

    [Fact]
    public async Task ASuccessorStartingTheSameDayItsPredecessorEndsIsAViolation()
    {
        // The whole point of the calendar-day rule: raw timestamps would call 09:00-after-08:00
        // clean, while the scheduler and the critical path both treat the day as taken. A green
        // Conflicts page contradicting a red critical path is the bug this guards.
        var conflicts = await DependencyConflictsAsync(
            predecessorEnd: Start.AddHours(1), successorStart: Start.AddHours(6));

        Assert.Contains(conflicts, c => c.Kind == ConflictKinds.DependencyViolation);
    }

    [Fact]
    public async Task ASuccessorStartingTheNextDayIsClean()
    {
        var conflicts = await DependencyConflictsAsync(
            predecessorEnd: Start, successorStart: Start.AddDays(1));

        Assert.DoesNotContain(conflicts, c => c.Kind == ConflictKinds.DependencyViolation);
    }

    [Fact]
    public async Task LagIsCeilingedToWholeDaysSoItNeverLetsASuccessorStartEarly()
    {
        // 90 minutes of lag ceilings to one whole day, so the next day is still too early.
        var tooEarly = await DependencyConflictsAsync(
            predecessorEnd: Start, successorStart: Start.AddDays(1), lagMinutes: 90);
        Assert.Contains(tooEarly, c => c.Kind == ConflictKinds.DependencyViolation);
        Assert.Contains("gap", Assert.Single(tooEarly, c => c.Kind == ConflictKinds.DependencyViolation).Message);

        var clean = await DependencyConflictsAsync(
            predecessorEnd: Start, successorStart: Start.AddDays(2), lagMinutes: 90);
        Assert.DoesNotContain(clean, c => c.Kind == ConflictKinds.DependencyViolation);
    }

    [Fact]
    public async Task AnUnscheduledPredecessorIsAWarningRatherThanAnError()
    {
        // An unscheduled predecessor is never in the scheduled batch, so it has to be resolved
        // off-batch. Nothing is wrong yet — there is simply no date to compare against, so it
        // must not read as a violation the planner has to fix.
        var predecessorId = Guid.NewGuid();
        var successorId = Guid.NewGuid();
        var successor = ScheduledRequest(successorId, [], Start.AddDays(5), Start.AddDays(5).AddHours(1));

        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([successor]);
        _requestRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(predecessorId, [], Start, Start.AddHours(1)) with
            {
                StartTs = null,
                EndTs = null,
            }]);
        _dependencyRepo
            .Setup(r => r.GetBySuccessorsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Edge(predecessorId, successorId)]);

        var result = await _service.GetAllAsync();

        var conflict = Assert.Single(
            result.Single(e => e.RequestId == successorId).Conflicts,
            c => c.Kind == ConflictKinds.DependencyViolation);
        Assert.Equal(ConflictSeverities.Warning, conflict.Severity);
        Assert.Contains("not scheduled", conflict.Message);
    }

    [Fact]
    public async Task APredecessorThatCannotBeResolvedAtAllIsStillReported()
    {
        // The edge outlives visibility of its endpoint. Reporting it as unscheduled beats
        // silently dropping the only signal that something upstream is missing.
        var predecessorId = Guid.NewGuid();
        var successorId = Guid.NewGuid();

        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ScheduledRequest(successorId, [], Start, Start.AddHours(1))]);
        _dependencyRepo
            .Setup(r => r.GetBySuccessorsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Edge(predecessorId, successorId)]);

        var result = await _service.GetAllAsync();

        var conflict = Assert.Single(
            result.Single(e => e.RequestId == successorId).Conflicts,
            c => c.Kind == ConflictKinds.DependencyViolation);
        Assert.Equal(ConflictSeverities.Warning, conflict.Severity);
    }

    [Fact]
    public async Task AnUnscheduledSuccessorHasNoPrecedenceConflictToReport()
    {
        // No start date means nothing to compare; the edge is not yet violated.
        var predecessorId = Guid.NewGuid();
        var successorId = Guid.NewGuid();
        var successor = ScheduledRequest(successorId, [], Start, Start.AddHours(1)) with
        {
            StartTs = null,
            EndTs = null,
        };

        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([successor]);
        _dependencyRepo
            .Setup(r => r.GetBySuccessorsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Edge(predecessorId, successorId)]);

        var result = await _service.GetAllAsync();

        Assert.DoesNotContain(
            result.SelectMany(e => e.Conflicts),
            c => c.Kind == ConflictKinds.DependencyViolation);
    }

    private static AssignmentValidationBatchItem BatchWarn(Guid requestId, Guid resourceId, params ValidationIssue[] warnings) => new()
    {
        RequestId = requestId,
        ResourceId = resourceId,
        Result = new ValidationResult
        {
            Severity = warnings.Length > 0 ? ValidationSeverity.Warning : ValidationSeverity.Ok,
            Blockers = [],
            Warnings = [.. warnings],
        },
    };

    // ── Join conditions ───────────────────────────────────────────────────────
    // Under "all" every unsatisfied edge is reported on its own, as before. Under "any" or
    // k-of-n the shortfall belongs to the whole incoming set, so reporting per edge would flag
    // a plan that is in fact correct.

    /// <summary>Two predecessors, one successor, with the successor's join condition varied.</summary>
    private async Task<IReadOnlyList<ConflictInfo>> TwoPredecessorConflictsAsync(
        PredecessorLogic logic,
        DateTime successorStart,
        int? k = null,
        bool secondPredecessorScheduled = true,
        RequestStatus secondPredecessorStatus = RequestStatus.New)
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid(), successorId = Guid.NewGuid();

        // First finishes early, second finishes late.
        var firstPred = ScheduledRequest(first, [], Start, Start.AddDays(1));
        var secondPred = ScheduledRequest(second, [], Start.AddDays(5), Start.AddDays(6),
            status: secondPredecessorStatus);
        var successor = ScheduledRequest(
            successorId, [], successorStart, successorStart.AddHours(1), logic: logic, k: k);

        var scheduled = secondPredecessorScheduled
            ? new List<RequestInfo> { firstPred, secondPred, successor }
            : [firstPred, successor];

        _requestRepo.Setup(r => r.GetScheduledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduled);

        // An unscheduled predecessor still EXISTS — ConflictService backfills it through
        // GetByIdsAsync with null dates. Only a deleted row is genuinely absent, and the two mean
        // opposite things: one can still be placed, the other never will be.
        if (!secondPredecessorScheduled)
            _requestRepo.Setup(r => r.GetByIdsAsync(
                    It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([Unscheduled(second)]);
        _dependencyRepo
            .Setup(r => r.GetBySuccessorsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Edge(first, successorId), Edge(second, successorId)]);

        var result = await _service.GetAllAsync();
        return result.SingleOrDefault(e => e.RequestId == successorId)?.Conflicts ?? [];
    }

    [Fact]
    public async Task AnAnyJoinSatisfiedByOnePredecessorReportsNothing()
    {
        // Starts after the FIRST predecessor finishes but well before the second. Under "all"
        // this is a violation; under "any" it is exactly what the user asked for.
        var conflicts = await TwoPredecessorConflictsAsync(
            PredecessorLogic.Any, successorStart: Start.AddDays(2));

        Assert.DoesNotContain(conflicts, c => c.Kind == ConflictKinds.DependencyViolation);
    }

    [Fact]
    public async Task TheSameStartUnderAnAllJoinIsStillAnError()
    {
        // The control for the test above: only the condition changed.
        var conflicts = await TwoPredecessorConflictsAsync(
            PredecessorLogic.All, successorStart: Start.AddDays(2));

        Assert.Contains(conflicts, c =>
            c.Kind == ConflictKinds.DependencyViolation && c.Severity == ConflictSeverities.Error);
    }

    [Fact]
    public async Task AnUnmetPartialJoinNamesHowManyAreNeeded()
    {
        // Needs one of two, respects neither — the wording has to say "1 of 2", not "all".
        var conflicts = await TwoPredecessorConflictsAsync(
            PredecessorLogic.KOfN, successorStart: Start, k: 1);

        var conflict = Assert.Single(conflicts, c => c.Kind == ConflictKinds.DependencyViolation);
        Assert.Contains("1 of 2 predecessors must be done; 0 are", conflict.Message);
    }

    [Fact]
    public async Task AnUnmetJoinWithAnUnscheduledPredecessorIsOnlyAWarning()
    {
        // The second predecessor has no dates yet, so placing it could still satisfy the join.
        // That is a plan in progress, not a broken one.
        var conflicts = await TwoPredecessorConflictsAsync(
            PredecessorLogic.KOfN, successorStart: Start, k: 1, secondPredecessorScheduled: false);

        var conflict = Assert.Single(conflicts, c => c.Kind == ConflictKinds.DependencyViolation);
        Assert.Equal(ConflictSeverities.Warning, conflict.Severity);
    }

    [Fact]
    public async Task AKOfNJoinThatNeedsEveryPredecessorReportsPerEdgeLikeAll()
    {
        // k=2 over 2 predecessors IS "all", so it must report the same way — one conflict per
        // offending edge, each naming its peer — rather than losing the peer links to an
        // aggregate message just because the stored logic says k_of_n.
        var conflicts = await TwoPredecessorConflictsAsync(
            PredecessorLogic.KOfN, successorStart: Start, k: 2);

        var violations = conflicts.Where(c => c.Kind == ConflictKinds.DependencyViolation).ToList();
        violations.Should().HaveCountGreaterThan(1);
        violations.Should().OnlyContain(c => c.PeerRequestId != null);
    }

    [Fact]
    public async Task ACancelledPredecessorDoesNotHoldAnAllJoinShut()
    {
        // Successor starts after the live predecessor finishes, but before the cancelled one's
        // window. Counting abandoned work would report a violation the user could only clear by
        // deleting the edge and losing the record that it existed.
        var conflicts = await TwoPredecessorConflictsAsync(
            PredecessorLogic.All, successorStart: Start.AddDays(2),
            secondPredecessorStatus: RequestStatus.Cancelled);

        Assert.DoesNotContain(conflicts, c => c.Kind == ConflictKinds.DependencyViolation);
    }
}
