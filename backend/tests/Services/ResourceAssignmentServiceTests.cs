using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class ResourceAssignmentServiceTests
{
    private static readonly Guid ResourceId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();

    private static CreateResourceAssignmentRequest MakeRequest() => new()
    {
        RequestId = RequestId,
        ResourceId = ResourceId,
        StartUtc = new DateTime(2026, 6, 9, 8, 0, 0, DateTimeKind.Utc),
        EndUtc = new DateTime(2026, 6, 9, 16, 0, 0, DateTimeKind.Utc),
    };

    private static ResourceAssignmentInfo MakeCreatedAssignment() => new()
    {
        Id = Guid.NewGuid(),
        RequestId = RequestId,
        ResourceId = ResourceId,
        ResourceTypeKey = "person",
        StartUtc = new DateTime(2026, 6, 9, 8, 0, 0, DateTimeKind.Utc),
        EndUtc = new DateTime(2026, 6, 9, 16, 0, 0, DateTimeKind.Utc),
        AssignmentStatus = AssignmentStatuses.Planned,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static ValidationResult OkResult() => new()
    {
        Severity = ValidationSeverity.Ok,
        Blockers = [],
        Warnings = [],
    };

    private static ValidationResult BlockerResult(ValidationReasonCode code, string message) => new()
    {
        Severity = ValidationSeverity.Blocker,
        Blockers = [new ValidationIssue { Code = code, Message = message, ResourceId = ResourceId }],
        Warnings = [],
    };

    private static ValidationResult MixedBlockerResult() => new()
    {
        Severity = ValidationSeverity.Blocker,
        Blockers =
        [
            new ValidationIssue { Code = ValidationReasonCode.CapabilityMissing, Message = "Missing skill", ResourceId = ResourceId },
            new ValidationIssue { Code = ValidationReasonCode.OffTimeOverlap, Message = "Off-time overlap", ResourceId = ResourceId },
        ],
        Warnings = [],
    };

    private static IResourceAssignmentService BuildService(
        ValidationResult validationResult,
        ResourceAssignmentInfo? createdAssignment = null)
    {
        var validatorMock = new Mock<IResourceAssignmentValidator>();
        validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<ValidateResourceAssignmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var repoMock = new Mock<IResourceAssignmentRepository>();
        if (createdAssignment is not null)
        {
            repoMock
                .Setup(r => r.CreateAsync(It.IsAny<CreateResourceAssignmentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(createdAssignment);
        }

        return new ResourceAssignmentService(repoMock.Object, validatorMock.Object);
    }

    [Fact]
    public async Task CreateAsync_NoBlockers_AssignmentCreated()
    {
        var created = MakeCreatedAssignment();
        var service = BuildService(OkResult(), created);

        var (assignment, conflict) = await service.CreateAsync(MakeRequest());

        Assert.NotNull(assignment);
        Assert.Null(conflict);
        Assert.Equal(created.Id, assignment.Id);
    }

    [Fact]
    public async Task CreateAsync_CapabilityMissingOnly_AssignmentCreated()
    {
        // capability.missing is a soft blocker — the planner may override it.
        // The service must not reject the create; the assignment goes through.
        var created = MakeCreatedAssignment();
        var service = BuildService(
            BlockerResult(ValidationReasonCode.CapabilityMissing, "Missing skill"),
            created);

        var (assignment, conflict) = await service.CreateAsync(MakeRequest());

        Assert.NotNull(assignment);
        Assert.Null(conflict);
    }

    [Fact]
    public async Task CreateAsync_HardBlockerPresent_ReturnsConflictNoAssignment()
    {
        var service = BuildService(
            BlockerResult(ValidationReasonCode.OffTimeOverlap, "Off-time overlap"));

        var (assignment, conflict) = await service.CreateAsync(MakeRequest());

        Assert.Null(assignment);
        Assert.NotNull(conflict);
    }

    [Fact]
    public async Task CreateAsync_ResourceInactiveBlocker_ReturnsConflictNoAssignment()
    {
        var service = BuildService(
            BlockerResult(ValidationReasonCode.ResourceInactive, "Resource is inactive"));

        var (assignment, conflict) = await service.CreateAsync(MakeRequest());

        Assert.Null(assignment);
        Assert.NotNull(conflict);
    }

    [Fact]
    public async Task CreateAsync_CapabilityMissingPlusHardBlocker_ReturnsConflictForHardBlocker()
    {
        // The hard blocker (off-time) must win even when capability.missing is also present.
        var service = BuildService(MixedBlockerResult());

        var (assignment, conflict) = await service.CreateAsync(MakeRequest());

        Assert.Null(assignment);
        Assert.NotNull(conflict);
        Assert.Equal(ResourceConflictType.OffTimeOverlap, conflict!.Type);
    }
}
