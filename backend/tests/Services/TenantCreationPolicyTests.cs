using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantCreationPolicyTests
{
    [Fact]
    public void EvaluateSlug_ShouldReturnReservedSlug_WhenSlugIsReserved()
    {
        var decision = TenantCreationPolicy.EvaluateSlug("admin");

        decision.Should().Be(TenantCreationDecision.ReservedSlug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("AUpper")]
    [InlineData("has_underscore")]
    [InlineData("ends-with-")]
    public void EvaluateSlug_ShouldReturnInvalidSlugFormat_WhenSlugIsInvalid(string slug)
    {
        var decision = TenantCreationPolicy.EvaluateSlug(slug);

        decision.Should().Be(TenantCreationDecision.InvalidSlugFormat);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("tenant-1")]
    [InlineData("orkyo1")]
    public void EvaluateSlug_ShouldReturnAllowed_WhenSlugIsValid(string slug)
    {
        var decision = TenantCreationPolicy.EvaluateSlug(slug);

        decision.Should().Be(TenantCreationDecision.Allowed);
    }

    [Fact]
    public void EvaluateOwnershipEligibility_ShouldReturnUserAlreadyOwnsTenant_WhenUserCannotCreate()
    {
        var decision = TenantCreationPolicy.EvaluateOwnershipEligibility(false);

        decision.Should().Be(TenantCreationDecision.UserAlreadyOwnsTenant);
    }

    [Fact]
    public void EvaluateOwnershipEligibility_ShouldReturnAllowed_WhenUserCanCreate()
    {
        var decision = TenantCreationPolicy.EvaluateOwnershipEligibility(true);

        decision.Should().Be(TenantCreationDecision.Allowed);
    }
}
