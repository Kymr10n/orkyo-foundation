using System.Text.Json;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Api.Tests.Services;

public class CapabilityMatcherTypedTests
{
    private readonly Mock<IResourceCapabilityRepository> _repoMock = new();
    private readonly CapabilityMatcher _matcher;

    public CapabilityMatcherTypedTests()
    {
        _matcher = new CapabilityMatcher(_repoMock.Object);
    }

    private static JsonElement JsonValue(object value) => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement;

    [Fact]
    public async Task NumberGreaterThanOrEqualOperator_Resource800GERequest500_ReturnsTrue()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue(500.0),
            Operator = ">=",
            AllowedValues = null,
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>
            {
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(),
                    ResourceId = resourceId,
                    CriterionId = criterionId,
                    Value = JsonValue(800.0),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata
                    {
                        Id = criterionId,
                        Name = "Weight",
                        DataType = CriterionDataType.Number,
                        Unit = "kg"
                    }
                }
            });

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.True(result);
    }

    [Fact]
    public async Task NumberEqualOperator_Strict_ReturnsTrueForExactMatch()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue(100.0),
            Operator = "=",
            AllowedValues = null,
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>
            {
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(),
                    ResourceId = resourceId,
                    CriterionId = criterionId,
                    Value = JsonValue(100.0),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata
                    {
                        Id = criterionId,
                        Name = "Capacity",
                        DataType = CriterionDataType.Number,
                        Unit = "units"
                    }
                }
            });

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.True(result);
    }

    [Fact]
    public async Task EnumMembership_ResourceValueInAllowedSet_ReturnsTrue()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();

        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue("L"),
            Operator = null,
            AllowedValues = JsonDocument.Parse("[\"S\",\"M\",\"L\",\"XL\"]").RootElement.Clone(),
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>
            {
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(),
                    ResourceId = resourceId,
                    CriterionId = criterionId,
                    Value = JsonValue("L"),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata
                    {
                        Id = criterionId,
                        Name = "Size",
                        DataType = CriterionDataType.Enum,
                        Unit = null
                    }
                }
            });

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.True(result);
    }

    [Fact]
    public async Task EnumMembership_ResourceValueNotInAllowedSet_ReturnsFalse()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();

        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue("L"),
            Operator = null,
            AllowedValues = JsonDocument.Parse("[\"S\",\"M\"]").RootElement.Clone(),
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>
            {
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(),
                    ResourceId = resourceId,
                    CriterionId = criterionId,
                    Value = JsonValue("L"),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata
                    {
                        Id = criterionId,
                        Name = "Size",
                        DataType = CriterionDataType.Enum,
                        Unit = null
                    }
                }
            });

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.False(result);
    }

    [Fact]
    public async Task BooleanTrue_ResourceHasTrue_ReturnsTrue()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue(true),
            Operator = null,
            AllowedValues = null,
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>
            {
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(),
                    ResourceId = resourceId,
                    CriterionId = criterionId,
                    Value = JsonValue(true),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata
                    {
                        Id = criterionId,
                        Name = "HasFeature",
                        DataType = CriterionDataType.Boolean,
                        Unit = null
                    }
                }
            });

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.True(result);
    }

    [Fact]
    public async Task BooleanTrue_ResourceHasFalse_ReturnsFalse()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue(true),
            Operator = null,
            AllowedValues = null,
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>
            {
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(),
                    ResourceId = resourceId,
                    CriterionId = criterionId,
                    Value = JsonValue(false),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata
                    {
                        Id = criterionId,
                        Name = "HasFeature",
                        DataType = CriterionDataType.Boolean,
                        Unit = null
                    }
                }
            });

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.False(result);
    }

    [Fact]
    public async Task StringCaseInsensitiveEquality_Matching_ReturnsTrue()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue("Test"),
            Operator = null,
            AllowedValues = null,
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>
            {
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(),
                    ResourceId = resourceId,
                    CriterionId = criterionId,
                    Value = JsonValue("TEST"),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata
                    {
                        Id = criterionId,
                        Name = "Name",
                        DataType = CriterionDataType.String,
                        Unit = null
                    }
                }
            });

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.True(result);
    }

    [Fact]
    public async Task CapabilityNotPresentOnResource_ReturnsFalse()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue(100),
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>());

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.False(result);
    }

    [Fact]
    public async Task NumberGreaterThanOrEqualOperator_ResourceBelowRequirement_ReturnsFalse()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue(2000.0), // 2 tonnes required
            Operator = ">=",
            AllowedValues = null,
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>
            {
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(),
                    ResourceId = resourceId,
                    CriterionId = criterionId,
                    Value = JsonValue(1000.0), // space has crane with 1 tonne
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata
                    {
                        Id = criterionId,
                        Name = "Crane capacity",
                        DataType = CriterionDataType.Number,
                        Unit = "kg"
                    }
                }
            });

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.False(result);
    }

    [Fact]
    public async Task NoOperatorOrAllowedValues_FallsBackToPresenceMatch_ReturnsTrue()
    {
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var requirement = new RequestRequirementInfo
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            CriterionId = criterionId,
            Value = JsonValue(100),
            Operator = null,
            AllowedValues = null,
            CreatedAt = DateTime.UtcNow
        };

        _repoMock.Setup(r => r.GetByResourceAsync(resourceId))
            .ReturnsAsync(new List<ResourceCapabilityInfo>
            {
                new ResourceCapabilityInfo
                {
                    Id = Guid.NewGuid(),
                    ResourceId = resourceId,
                    CriterionId = criterionId,
                    Value = JsonValue(200),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Criterion = new CriterionMetadata
                    {
                        Id = criterionId,
                        Name = "Value",
                        DataType = CriterionDataType.Number,
                        Unit = null
                    }
                }
            });

        var result = await _matcher.ResourceSatisfiesRequirementAsync(resourceId, requirement);
        Assert.True(result);
    }
}
