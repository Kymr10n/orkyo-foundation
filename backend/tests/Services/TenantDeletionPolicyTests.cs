using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantDeletionPolicyTests
{
    [Fact]
    public void EvaluateDelete_ShouldReturnTenantNotFound_WhenMissing()
    {
        var decision = TenantDeletionPolicy.EvaluateDelete(false, null, Guid.NewGuid(), false, null);

        decision.Should().Be(TenantDeleteDecision.TenantNotFound);
    }

    [Fact]
    public void EvaluateDelete_ShouldReturnNotOwner_WhenActorNotOwnerAndNotSiteAdmin()
    {
        var decision = TenantDeletionPolicy.EvaluateDelete(true, Guid.NewGuid(), Guid.NewGuid(), false, TenantStatusConstants.Active);

        decision.Should().Be(TenantDeleteDecision.NotOwner);
    }

    [Fact]
    public void EvaluateDelete_ShouldReturnAlreadyDeleting_WhenStatusDeleting()
    {
        var owner = Guid.NewGuid();
        var decision = TenantDeletionPolicy.EvaluateDelete(true, owner, owner, false, TenantStatusConstants.Deleting);

        decision.Should().Be(TenantDeleteDecision.AlreadyDeleting);
    }

    [Fact]
    public void EvaluateDelete_ShouldReturnAllowed_WhenOwnerAndNotDeleting()
    {
        var owner = Guid.NewGuid();
        var decision = TenantDeletionPolicy.EvaluateDelete(true, owner, owner, false, TenantStatusConstants.Active);

        decision.Should().Be(TenantDeleteDecision.Allowed);
    }

    [Fact]
    public void EvaluateCancelDeletion_ShouldReturnTenantNotFound_WhenMissing()
    {
        var decision = TenantDeletionPolicy.EvaluateCancelDeletion(false, null, Guid.NewGuid(), false, null);

        decision.Should().Be(TenantCancelDeletionDecision.TenantNotFound);
    }

    [Fact]
    public void EvaluateCancelDeletion_ShouldReturnNotOwner_WhenActorNotOwnerAndNotSiteAdmin()
    {
        var decision = TenantDeletionPolicy.EvaluateCancelDeletion(true, Guid.NewGuid(), Guid.NewGuid(), false, TenantStatusConstants.Deleting);

        decision.Should().Be(TenantCancelDeletionDecision.NotOwner);
    }

    [Fact]
    public void EvaluateCancelDeletion_ShouldReturnNotDeleting_WhenStatusNotDeleting()
    {
        var owner = Guid.NewGuid();
        var decision = TenantDeletionPolicy.EvaluateCancelDeletion(true, owner, owner, false, TenantStatusConstants.Active);

        decision.Should().Be(TenantCancelDeletionDecision.NotDeleting);
    }

    [Fact]
    public void EvaluateCancelDeletion_ShouldReturnAllowed_WhenOwnerAndDeleting()
    {
        var owner = Guid.NewGuid();
        var decision = TenantDeletionPolicy.EvaluateCancelDeletion(true, owner, owner, false, TenantStatusConstants.Deleting);

        decision.Should().Be(TenantCancelDeletionDecision.Allowed);
    }
}
