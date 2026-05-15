using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Api.Tests.Services;

public class ResourceAssignmentValidatorTests
{
    private readonly Mock<IResourceRepository> _resourceRepoMock = new();
    private readonly Mock<IResourceAssignmentRepository> _assignmentRepoMock = new();
    private readonly Mock<ICapabilityMatcher> _capabilityMatcherMock = new();
    private readonly Mock<IOffTimeResourceQuery> _offTimeQueryMock = new();
    private readonly Mock<IRequestRepository> _requestRepoMock = new();
    private readonly Mock<ISchedulingRepository> _schedulingRepoMock = new();
    private readonly ResourceAssignmentValidator _validator;

    public ResourceAssignmentValidatorTests()
    {
        // Default safe returns for collection-returning methods so individual tests
        // only need to override what they care about. Without these, Moq returns null
        // Tasks which cause NREs when the validator iterates the result.
        _assignmentRepoMock
            .Setup(r => r.GetOverlappingActiveAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ResourceAssignmentInfo>());
        _offTimeQueryMock
            .Setup(q => q.GetBlockingOffTimesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<OffTimeInfo>());

        _validator = new ResourceAssignmentValidator(
            _resourceRepoMock.Object,
            _assignmentRepoMock.Object,
            _capabilityMatcherMock.Object,
            _offTimeQueryMock.Object,
            _requestRepoMock.Object,
            _schedulingRepoMock.Object);
    }

    private static ValidateResourceAssignmentRequest CreateValidationRequest(
        Guid resourceId = default,
        Guid requestId = default,
        DateTime? startUtc = null,
        DateTime? endUtc = null,
        decimal? allocationPercent = null,
        string? allocationMode = null)
    {
        var start = startUtc ?? DateTime.UtcNow.AddDays(1);
        var end = endUtc ?? start.AddDays(5);
        return new ValidateResourceAssignmentRequest
        {
            RequestId = requestId == default ? Guid.NewGuid() : requestId,
            ResourceId = resourceId == default ? Guid.NewGuid() : resourceId,
            StartUtc = start,
            EndUtc = end,
            AllocationPercent = allocationPercent,
            AllocationMode = allocationMode
        };
    }

    private static ResourceInfo CreateResource(
        Guid id = default,
        bool isActive = true,
        string allocationMode = AllocationModes.Exclusive,
        int baseAvailabilityPercent = 100)
    {
        return new ResourceInfo
        {
            Id = id == default ? Guid.NewGuid() : id,
            ResourceTypeId = Guid.NewGuid(),
            ResourceTypeKey = "person",
            Name = "Test Resource",
            AllocationMode = allocationMode,
            BaseAvailabilityPercent = baseAvailabilityPercent,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static RequestRequirementInfo CreateRequirement(Guid criterionId = default)
    {
        return new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId == default ? Guid.NewGuid() : criterionId,
            Value = JsonDocument.Parse("100").RootElement,
            CreatedAt = DateTime.UtcNow,
            Criterion = new CriterionBasicInfo
            {
                Id = criterionId == default ? Guid.NewGuid() : criterionId,
                Name = "Test Criterion",
                DataType = CriterionDataType.Number
            }
        };
    }

    // ===== Reason Code Tests =====

    [Fact]
    public async Task ResourceNotFound_ReturnsBlocker()
    {
        var request = CreateValidationRequest();

        _resourceRepoMock.Setup(r => r.GetByIdAsync(request.ResourceId))
            .ReturnsAsync((ResourceInfo?)null);

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Blocker, result.Severity);
        Assert.Single(result.Blockers);
        Assert.Equal(ValidationReasonCode.ResourceNotFound, result.Blockers[0].Code);
    }

    [Fact]
    public async Task ResourceInactive_ReturnsBlocker()
    {
        var resource = CreateResource(isActive: false);
        var request = CreateValidationRequest(resourceId: resource.Id);

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Blocker, result.Severity);
        Assert.Single(result.Blockers);
        Assert.Equal(ValidationReasonCode.ResourceInactive, result.Blockers[0].Code);
    }

    [Fact]
    public async Task CapabilityMissing_ReturnsBlocker()
    {
        var resource = CreateResource();
        var request = CreateValidationRequest(resourceId: resource.Id);
        var requestInfo = new RequestInfo
        {
            Id = request.RequestId,
            Name = "test",
            PlanningMode = PlanningMode.Leaf,
            Status = RequestStatus.Planned,
            SchedulingSettingsApply = false,
            Requirements = new List<RequestRequirementInfo> { CreateRequirement() },
            Assignments = new List<ResourceAssignmentInfo>(),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true))
            .ReturnsAsync(requestInfo);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id))
            .ReturnsAsync((Guid?)null);
        _capabilityMatcherMock.Setup(c => c.ResourceSatisfiesRequirementAsync(
            resource.Id, It.IsAny<RequestRequirementInfo>()))
            .ReturnsAsync(false);

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Blocker, result.Severity);
        Assert.Single(result.Blockers);
        Assert.Equal(ValidationReasonCode.CapabilityMissing, result.Blockers[0].Code);
    }

    [Fact]
    public async Task OffTimeOverlap_ReturnsWarning()
    {
        var resource = CreateResource();
        var request = CreateValidationRequest(resourceId: resource.Id);

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true))
            .ReturnsAsync((RequestInfo?)null);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id))
            .ReturnsAsync((Guid?)Guid.NewGuid());
        _offTimeQueryMock.Setup(o => o.GetBlockingOffTimesAsync(
            resource.Id, It.IsAny<Guid>(), request.StartUtc, request.EndUtc))
            .ReturnsAsync(new List<OffTimeInfo>
            {
                new OffTimeInfo
                {
                    Id = Guid.NewGuid(),
                    SiteId = Guid.NewGuid(),
                    Title = "Scheduled maintenance",
                    Type = OffTimeType.Maintenance, // non-holiday → generic OffTimeOverlap
                    AppliesToAllResources = false,
                    ResourceIds = new List<Guid> { resource.Id },
                    StartTs = request.StartUtc.AddDays(-1),
                    EndTs = request.EndUtc.AddDays(1),
                    IsRecurring = false,
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            });

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Warning, result.Severity);
        Assert.Single(result.Warnings);
        Assert.Equal(ValidationReasonCode.OffTimeOverlap, result.Warnings[0].Code);
    }

    [Fact]
    public async Task AssignmentOverbooked_ExclusiveOverlap_ReturnsBlocker()
    {
        var resource = CreateResource(allocationMode: AllocationModes.Exclusive);
        var request = CreateValidationRequest(resourceId: resource.Id);

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true))
            .ReturnsAsync((RequestInfo?)null);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id))
            .ReturnsAsync((Guid?)null);
        _assignmentRepoMock.Setup(a => a.GetOverlappingActiveAsync(
            resource.Id, request.StartUtc, request.EndUtc))
            .ReturnsAsync(new List<ResourceAssignmentInfo>
            {
                new ResourceAssignmentInfo
                {
                    Id = Guid.NewGuid(),
                    RequestId = Guid.NewGuid(),
                    ResourceId = resource.Id,
                    ResourceTypeKey = "person",
                    StartUtc = request.StartUtc.AddDays(-1),
                    EndUtc = request.EndUtc.AddDays(1),
                    AllocationPercent = null,
                    AssignmentStatus = "Active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            });

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Blocker, result.Severity);
        Assert.Single(result.Blockers);
        Assert.Equal(ValidationReasonCode.AssignmentOverbooked, result.Blockers[0].Code);
    }

    [Fact]
    public async Task AssignmentOverbooked_FractionalCapacityExceeded_ReturnsBlocker()
    {
        var resource = CreateResource(
            allocationMode: AllocationModes.Fractional,
            baseAvailabilityPercent: 100);
        var request = CreateValidationRequest(
            resourceId: resource.Id,
            allocationPercent: 60m);

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true))
            .ReturnsAsync((RequestInfo?)null);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id))
            .ReturnsAsync((Guid?)null);
        _assignmentRepoMock.Setup(a => a.GetTotalAllocatedPercentAsync(
            resource.Id, request.StartUtc, request.EndUtc))
            .ReturnsAsync(50m); // 50% already allocated, 60% more = 110% > 100%

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Blocker, result.Severity);
        Assert.Single(result.Blockers);
        Assert.Equal(ValidationReasonCode.AssignmentOverbooked, result.Blockers[0].Code);
    }

    [Fact]
    public async Task NonWorkingWeekend_ReturnsWarning()
    {
        // Saturday 2026-01-03 → 14:00 UTC; site has weekends_enabled=false.
        var resource = CreateResource();
        var siteId = Guid.NewGuid();
        var request = CreateValidationRequest(
            resourceId: resource.Id,
            startUtc: new DateTime(2026, 1, 3, 14, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2026, 1, 3, 16, 0, 0, DateTimeKind.Utc));

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true)).ReturnsAsync((RequestInfo?)null);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id)).ReturnsAsync(siteId);
        _schedulingRepoMock.Setup(s => s.GetSettingsAsync(siteId))
            .ReturnsAsync(SchedulingSettingsInfo.Default(siteId) with { WeekendsEnabled = false });

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Warning, result.Severity);
        Assert.Contains(result.Warnings, w => w.Code == ValidationReasonCode.NonWorkingWeekend);
    }

    [Fact]
    public async Task NonWorkingWeekend_WeekendsEnabled_DoesNotWarn()
    {
        // Same Saturday request, but site allows weekend work → no weekend warning.
        var resource = CreateResource();
        var siteId = Guid.NewGuid();
        var request = CreateValidationRequest(
            resourceId: resource.Id,
            startUtc: new DateTime(2026, 1, 3, 14, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2026, 1, 3, 16, 0, 0, DateTimeKind.Utc));

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true)).ReturnsAsync((RequestInfo?)null);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id)).ReturnsAsync(siteId);
        _schedulingRepoMock.Setup(s => s.GetSettingsAsync(siteId))
            .ReturnsAsync(SchedulingSettingsInfo.Default(siteId) with { WeekendsEnabled = true });

        var result = await _validator.ValidateAsync(request);

        Assert.DoesNotContain(result.Warnings, w => w.Code == ValidationReasonCode.NonWorkingWeekend);
    }

    [Fact]
    public async Task NonWorkingHoliday_ReturnsWarning()
    {
        // An off-time row with Type=Holiday should surface as a NonWorkingHoliday warning,
        // not the generic OffTimeOverlap, so the UI can label it correctly.
        var resource = CreateResource();
        var siteId = Guid.NewGuid();
        var request = CreateValidationRequest(resourceId: resource.Id);

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true)).ReturnsAsync((RequestInfo?)null);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id)).ReturnsAsync(siteId);
        _offTimeQueryMock
            .Setup(q => q.GetBlockingOffTimesAsync(resource.Id, siteId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<OffTimeInfo>
            {
                new() {
                    Id = Guid.NewGuid(),
                    SiteId = siteId,
                    Type = OffTimeType.Holiday,
                    Title = "Independence Day",
                    StartTs = request.StartUtc,
                    EndTs = request.EndUtc,
                    AppliesToAllResources = true,
                    IsRecurring = false,
                    Enabled = true,
                }
            });

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Warning, result.Severity);
        Assert.Contains(result.Warnings, w => w.Code == ValidationReasonCode.NonWorkingHoliday);
        Assert.DoesNotContain(result.Warnings, w => w.Code == ValidationReasonCode.OffTimeOverlap);
    }

    [Fact]
    public async Task InvalidAllocationMode_ReturnsBlocker()
    {
        var resource = CreateResource(allocationMode: AllocationModes.ConcurrentCapacity);
        var request = CreateValidationRequest(
            resourceId: resource.Id,
            allocationMode: AllocationModes.ConcurrentCapacity);

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true))
            .ReturnsAsync((RequestInfo?)null);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id))
            .ReturnsAsync((Guid?)null);

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Blocker, result.Severity);
        Assert.Single(result.Blockers);
        Assert.Equal(ValidationReasonCode.InvalidAllocationMode, result.Blockers[0].Code);
    }

    [Fact]
    public async Task InvalidAllocationPercent_ExclusiveWithPct_ReturnsBlocker()
    {
        var resource = CreateResource(allocationMode: AllocationModes.Exclusive);
        var request = CreateValidationRequest(
            resourceId: resource.Id,
            allocationPercent: 50m);

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true))
            .ReturnsAsync((RequestInfo?)null);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id))
            .ReturnsAsync((Guid?)null);

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Blocker, result.Severity);
        Assert.Single(result.Blockers);
        Assert.Equal(ValidationReasonCode.InvalidAllocationPercent, result.Blockers[0].Code);
    }

    [Fact]
    public async Task InvalidAllocationPercent_FractionalWithoutPct_ReturnsBlocker()
    {
        var resource = CreateResource(allocationMode: AllocationModes.Fractional);
        var request = CreateValidationRequest(
            resourceId: resource.Id,
            allocationPercent: null);

        _resourceRepoMock.Setup(r => r.GetByIdAsync(resource.Id)).ReturnsAsync(resource);
        _requestRepoMock.Setup(r => r.GetByIdAsync(request.RequestId, true))
            .ReturnsAsync((RequestInfo?)null);
        _schedulingRepoMock.Setup(s => s.GetSiteIdForResourceAsync(resource.Id))
            .ReturnsAsync((Guid?)null);

        var result = await _validator.ValidateAsync(request);

        Assert.Equal(ValidationSeverity.Blocker, result.Severity);
        Assert.Single(result.Blockers);
        Assert.Equal(ValidationReasonCode.InvalidAllocationPercent, result.Blockers[0].Code);
    }

    // ===== Integration Tests =====

    [Fact]
    public async Task CreateAsync_RejectsWhenValidatorReturnsBlocker()
    {
        // This test verifies that CreateAsync rejects when the validator says Blocker
        var validatorMock = new Mock<IResourceAssignmentValidator>();
        var service = new ResourceAssignmentService(
            _assignmentRepoMock.Object,
            validatorMock.Object);

        var request = new CreateResourceAssignmentRequest
        {
            RequestId = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(),
            StartUtc = DateTime.UtcNow.AddDays(1),
            EndUtc = DateTime.UtcNow.AddDays(5),
            AllocationPercent = null
        };

        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<ValidateResourceAssignmentRequest>()))
            .ReturnsAsync(new ValidationResult
            {
                Severity = ValidationSeverity.Blocker,
                Blockers = new List<ValidationIssue>
                {
                    new ValidationIssue
                    {
                        Code = ValidationReasonCode.ResourceNotFound,
                        Message = "Resource not found",
                        ResourceId = request.ResourceId
                    }
                },
                Warnings = new List<ValidationIssue>()
            });

        var result = await service.CreateAsync(request);

        Assert.Null(result.Assignment);
        Assert.NotNull(result.Conflict);
        Assert.Equal(ResourceConflictType.ResourceNotFound, result.Conflict.Type);
    }

    [Fact]
    public async Task CreateAsync_SucceedsWhenValidatorReturnsOk()
    {
        var validatorMock = new Mock<IResourceAssignmentValidator>();
        var service = new ResourceAssignmentService(
            _assignmentRepoMock.Object,
            validatorMock.Object);

        var request = new CreateResourceAssignmentRequest
        {
            RequestId = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(),
            StartUtc = DateTime.UtcNow.AddDays(1),
            EndUtc = DateTime.UtcNow.AddDays(5),
            AllocationPercent = null
        };

        var createdAssignment = new ResourceAssignmentInfo
        {
            Id = Guid.NewGuid(),
            RequestId = request.RequestId,
            ResourceId = request.ResourceId,
            ResourceTypeKey = "person",
            StartUtc = request.StartUtc,
            EndUtc = request.EndUtc,
            AllocationPercent = null,
            AssignmentStatus = "Active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<ValidateResourceAssignmentRequest>()))
            .ReturnsAsync(new ValidationResult
            {
                Severity = ValidationSeverity.Ok,
                Blockers = new List<ValidationIssue>(),
                Warnings = new List<ValidationIssue>()
            });

        _assignmentRepoMock.Setup(a => a.CreateAsync(It.IsAny<CreateResourceAssignmentRequest>()))
            .ReturnsAsync(createdAssignment);

        var result = await service.CreateAsync(request);

        Assert.NotNull(result.Assignment);
        Assert.Null(result.Conflict);
    }
}
