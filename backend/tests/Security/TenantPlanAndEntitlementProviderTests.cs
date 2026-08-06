using Api.Security.Features;
using AwesomeAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Security;

/// <summary>
/// Pins the plan/entitlement contract foundation puts on the wire.
///
/// These exist because the code-vs-label distinction was documented but never asserted:
/// /me shipped the display label ("Enterprise") where the SPA compares literal lowercase
/// codes, so every tier-gated feature silently presented as unavailable.
/// </summary>
public class SinglePlanInfoProviderTests
{
    [Fact]
    public void PlanCode_IsLowercaseAndDistinctFromLabel()
    {
        SinglePlanInfoProvider.PlanCode.Should().Be("community");
        SinglePlanInfoProvider.PlanCode.Should().Be(SinglePlanInfoProvider.PlanCode.ToLowerInvariant(),
            "clients compare plan codes case-sensitively");
        SinglePlanInfoProvider.PlanCode.Should().NotBe(SinglePlanInfoProvider.PlanLabel,
            "code and label must stay distinguishable, or sending the wrong one looks correct");
    }

    [Fact]
    public async Task GetPlanInfoAsync_ReturnsOneEntryPerRequestedTenant()
    {
        var tenantIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var result = await new SinglePlanInfoProvider().GetPlanInfoAsync(tenantIds);

        result.Should().HaveCount(2);
        result.Values.Should().OnlyContain(
            info => info.PlanCode == SinglePlanInfoProvider.PlanCode
                 && info.PlanLabel == SinglePlanInfoProvider.PlanLabel);
    }
}

public class AllFeaturesEntitlementProviderTests
{
    [Fact]
    public async Task GetEntitlementsAsync_EnablesEveryEnforcedFeature()
    {
        var tenantId = Guid.NewGuid();

        var result = await new AllFeaturesEntitlementProvider().GetEntitlementsAsync(new[] { tenantId });

        result.Should().ContainKey(tenantId);
        result[tenantId].Keys.Should().BeEquivalentTo(FeatureKeys.Enforced,
            "Community enables everything, and the reported key set is FeatureKeys.Enforced");
        result[tenantId].Values.Should().OnlyContain(enabled => enabled);
    }

    [Fact]
    public void EnforcedKeys_ExcludeAutoSchedule()
    {
        // auto_schedule has no entitlement row and no endpoint gate, so reporting it would
        // hand clients a default-denied false and hide the feature from paying tenants.
        FeatureKeys.Enforced.Should().NotContain(FeatureKeys.AutoSchedule);
        FeatureKeys.Enforced.Should().BeEquivalentTo(new[]
        {
            FeatureKeys.ApiAccess,
            FeatureKeys.AuditLog,
            FeatureKeys.DataExport,
            FeatureKeys.CalendarFeed,
        });
    }
}
