using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantCreationPolicyTests
{
    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]   // reserved regardless of case, before the format check
    public void EvaluateSlug_ShouldReturnReservedSlug_WhenSlugIsReserved(string slug)
    {
        var decision = TenantCreationPolicy.EvaluateSlug(slug);

        decision.Should().Be(TenantCreationDecision.ReservedSlug);
    }

    [Theory]
    [InlineData(63, TenantCreationDecision.Allowed)]
    [InlineData(64, TenantCreationDecision.InvalidSlugFormat)]
    public void EvaluateSlug_BoundsTheLengthAt63(int length, TenantCreationDecision expected)
    {
        TenantCreationPolicy.EvaluateSlug(new string('a', length)).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("AUpper")]
    [InlineData("has_underscore")]
    [InlineData("ends-with-")]
    [InlineData("1abc")]       // leading digit
    [InlineData("tenant\n")]  // `$` matched before a trailing newline
    [InlineData(null)]         // threw in the reserved-set lookup
    public void EvaluateSlug_ShouldReturnInvalidSlugFormat_WhenSlugIsInvalid(string? slug)
    {
        var decision = TenantCreationPolicy.EvaluateSlug(slug!);

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
