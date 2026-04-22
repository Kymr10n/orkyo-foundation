using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantUpdatePolicyTests
{
    [Fact]
    public void Evaluate_ShouldReturnTenantNotFound_WhenTenantMissing()
    {
        var decision = TenantUpdatePolicy.Evaluate(false, null, Guid.NewGuid(), false, null, null);

        decision.Should().Be(TenantUpdateDecision.TenantNotFound);
    }

    [Fact]
    public void Evaluate_ShouldReturnNotOwner_WhenActorNotOwnerAndNotSiteAdmin()
    {
        var decision = TenantUpdatePolicy.Evaluate(true, Guid.NewGuid(), Guid.NewGuid(), false, TenantStatusConstants.Active, null);

        decision.Should().Be(TenantUpdateDecision.NotOwner);
    }

    [Fact]
    public void Evaluate_ShouldReturnTenantDeleting_WhenStatusDeleting()
    {
        var owner = Guid.NewGuid();
        var decision = TenantUpdatePolicy.Evaluate(true, owner, owner, false, TenantStatusConstants.Deleting, null);

        decision.Should().Be(TenantUpdateDecision.TenantDeleting);
    }

    [Fact]
    public void Evaluate_ShouldReturnEmptyDisplayName_WhenWhitespaceProvided()
    {
        var owner = Guid.NewGuid();
        var decision = TenantUpdatePolicy.Evaluate(true, owner, owner, false, TenantStatusConstants.Active, "   ");

        decision.Should().Be(TenantUpdateDecision.EmptyDisplayName);
    }

    [Fact]
    public void Evaluate_ShouldReturnDisplayNameTooLong_WhenLengthExceeds255()
    {
        var owner = Guid.NewGuid();
        var decision = TenantUpdatePolicy.Evaluate(true, owner, owner, false, TenantStatusConstants.Active, new string('a', 256));

        decision.Should().Be(TenantUpdateDecision.DisplayNameTooLong);
    }

    [Fact]
    public void Evaluate_ShouldReturnAllowed_WhenValidAndAuthorized()
    {
        var owner = Guid.NewGuid();
        var decision = TenantUpdatePolicy.Evaluate(true, owner, owner, false, TenantStatusConstants.Active, "Valid Name");

        decision.Should().Be(TenantUpdateDecision.Allowed);
    }
}
