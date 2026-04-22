using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnerStatusDecisionFlowTests
{
    [Fact]
    public void EvaluateDelete_ShouldReturnTenantNotFound_WhenTenantMissing()
    {
        var snapshot = new TenantOwnerStatusSnapshot(null, null, false);

        var decision = TenantOwnerStatusDecisionFlow.EvaluateDelete(snapshot, Guid.NewGuid(), false);

        decision.Should().Be(TenantDeleteDecision.TenantNotFound);
    }

    [Fact]
    public void EvaluateDelete_ShouldReturnAlreadyDeleting_WhenTenantDeleting()
    {
        var actor = Guid.NewGuid();
        var snapshot = new TenantOwnerStatusSnapshot(actor, TenantStatusConstants.Deleting, true);

        var decision = TenantOwnerStatusDecisionFlow.EvaluateDelete(snapshot, actor, false);

        decision.Should().Be(TenantDeleteDecision.AlreadyDeleting);
    }

    [Fact]
    public void EvaluateCancelDeletion_ShouldReturnNotDeleting_WhenStatusIsActive()
    {
        var actor = Guid.NewGuid();
        var snapshot = new TenantOwnerStatusSnapshot(actor, TenantStatusConstants.Active, true);

        var decision = TenantOwnerStatusDecisionFlow.EvaluateCancelDeletion(snapshot, actor, false);

        decision.Should().Be(TenantCancelDeletionDecision.NotDeleting);
    }

    [Fact]
    public void EvaluateUpdate_ShouldReturnEmptyDisplayName_WhenWhitespaceProvided()
    {
        var actor = Guid.NewGuid();
        var snapshot = new TenantOwnerStatusSnapshot(actor, TenantStatusConstants.Active, true);

        var decision = TenantOwnerStatusDecisionFlow.EvaluateUpdate(snapshot, actor, false, "   ");

        decision.Should().Be(TenantUpdateDecision.EmptyDisplayName);
    }

    [Fact]
    public void EvaluateUpdate_ShouldReturnAllowed_WhenOwnerAndValidDisplayName()
    {
        var actor = Guid.NewGuid();
        var snapshot = new TenantOwnerStatusSnapshot(actor, TenantStatusConstants.Active, true);

        var decision = TenantOwnerStatusDecisionFlow.EvaluateUpdate(snapshot, actor, false, "Acme");

        decision.Should().Be(TenantUpdateDecision.Allowed);
    }
}
