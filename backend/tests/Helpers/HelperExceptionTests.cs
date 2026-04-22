using Api.Helpers;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Helpers;

public class HelperExceptionTests
{
    [Fact]
    public void FeatureNotAvailableException_WithReason_ShouldExposeFeatureAndFormattedMessage()
    {
        var ex = new FeatureNotAvailableException("auto_schedule", "disabled for this org");

        ex.Feature.Should().Be("auto_schedule");
        ex.Message.Should().Be("Feature 'auto_schedule' is not available: disabled for this org");
    }

    [Fact]
    public void FeatureNotAvailableException_WithTierRequirement_ShouldExposeFeatureAndFormattedMessage()
    {
        var ex = new FeatureNotAvailableException("auto_schedule", "free", "professional");

        ex.Feature.Should().Be("auto_schedule");
        ex.Message.Should().Be("Feature 'auto_schedule' requires professional tier or above. Current tier: free.");
    }

    [Fact]
    public void QuotaExceededException_DefaultConstructor_ShouldExposeResourceLimitAndMessage()
    {
        var ex = new QuotaExceededException("sites", 5);

        ex.ResourceType.Should().Be("sites");
        ex.Limit.Should().Be(5);
        ex.Message.Should().Be("Quota exceeded for sites. Maximum allowed: 5");
    }

    [Fact]
    public void QuotaExceededException_CustomMessageConstructor_ShouldPreserveCustomMessage()
    {
        var ex = new QuotaExceededException("spaces", 15, "Custom quota message");

        ex.ResourceType.Should().Be("spaces");
        ex.Limit.Should().Be(15);
        ex.Message.Should().Be("Custom quota message");
    }

    [Fact]
    public void TierLimitExceededException_ShouldInheritQuotaPropertiesAndExposeTierName()
    {
        var ex = new TierLimitExceededException("free", "spaces", 15);

        ex.Should().BeAssignableTo<QuotaExceededException>();
        ex.TierName.Should().Be("free");
        ex.ResourceType.Should().Be("spaces");
        ex.Limit.Should().Be(15);
        ex.Message.Should().Be("Service tier 'free' limit exceeded for spaces. Maximum allowed: 15");
    }
}
