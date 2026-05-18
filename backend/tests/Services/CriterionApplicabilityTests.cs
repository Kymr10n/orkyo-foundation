using Api.Helpers;
using Api.Models;
using Xunit;

namespace Api.Tests.Services;

/// <summary>
/// Tests for Criterion Applicability validation (Phase 3)
/// </summary>
public class CriterionApplicabilityTests
{
    [Fact]
    public void CapabilityNotApplicableException_HasCorrectProperties()
    {
        // Arrange
        var resourceId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var message = "Criterion is not applicable to this resource type";

        // Act
        var exception = new CapabilityNotApplicableException(resourceId, criterionId, message);

        // Assert
        Assert.Equal(resourceId, exception.ResourceId);
        Assert.Equal(criterionId, exception.CriterionId);
        Assert.Equal(message, exception.Message);
    }
}
