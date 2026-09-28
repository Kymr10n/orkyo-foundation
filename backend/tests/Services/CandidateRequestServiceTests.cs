using System.Text.Json;
using Api.Models;
using Api.Repositories;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// A resource's candidate requests, each requirement marked satisfied or not. The endpoint used
/// to await the matcher per requirement of every candidate, re-reading the same capability set
/// each time.
/// </summary>
public class CandidateRequestServiceTests
{
    private readonly Mock<IResourceService> _resources = new();
    private readonly Mock<IRequestRepository> _requests = new();
    private readonly Mock<IResourceCapabilityRepository> _capabilities = new();
    private static readonly Guid ResourceId = Guid.NewGuid();
    private static readonly Guid Owned = Guid.NewGuid();
    private static readonly Guid Missing = Guid.NewGuid();

    private CandidateRequestService CreateSut() =>
        new(_resources.Object, _requests.Object, _capabilities.Object, new CapabilityMatcher());

    private static RequestRequirementInfo Requirement(Guid criterionId, params string[] scope) => new()
    {
        Id = Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        CriterionId = criterionId,
        Value = JsonSerializer.SerializeToElement(true),
        CreatedAt = DateTime.UtcNow,
        Criterion = new CriterionBasicInfo
        {
            Id = criterionId,
            Name = criterionId == Owned ? "Forklift" : "Crane",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = scope,
        },
    };

    private static RequestInfo Request(params RequestRequirementInfo[] requirements) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Job",
        PlanningMode = PlanningMode.Leaf,
        Status = RequestStatus.New,
        SchedulingSettingsApply = false,
        Requirements = requirements.ToList(),
        Assignments = [],
        TargetResourceTypeKeys = ["person"],
        MinimalDurationValue = 1,
        MinimalDurationUnit = DurationUnit.Hours,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task MatchesEveryCandidate_AgainstOneCapabilityRead()
    {
        _resources.Setup(r => r.GetByIdAsync(ResourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceInfo
            {
                Id = ResourceId,
                Name = "Ann",
                ResourceTypeKey = "person",
                ResourceTypeId = Guid.NewGuid(),
                AllocationMode = "Fractional",
                BaseAvailabilityPercent = 100,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        _requests.Setup(r => r.GetCandidatesOverlappingAsync(ResourceId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                (Request(Requirement(Owned, "person"), Requirement(Missing, "person")), null),
                (Request(Requirement(Owned, "person"), Requirement(Missing, "machine")), Guid.NewGuid()),
            ]);
        _capabilities.Setup(c => c.GetByResourceAsync(ResourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(), ResourceId = ResourceId, CriterionId = Owned,
                    Value = JsonSerializer.SerializeToElement(true), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata { Id = Owned, Name = "Forklift", DataType = CriterionDataType.Boolean },
                },
            ]);

        var result = await CreateSut().GetForResourceAsync(ResourceId, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

        result.Should().HaveCount(2);
        result![0].Requirements.Should().BeEquivalentTo(
            [new CandidateRequirementInfo("Forklift", true), new CandidateRequirementInfo("Crane", false)]);
        // A requirement scoped to another type is not this resource's to satisfy.
        result[1].Requirements.Should().ContainSingle().Which.Label.Should().Be("Forklift");
        _capabilities.Verify(c => c.GetByResourceAsync(ResourceId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnUnknownResource_IsNull()
    {
        (await CreateSut().GetForResourceAsync(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddHours(1)))
            .Should().BeNull();
        _requests.VerifyNoOtherCalls();
    }
}
