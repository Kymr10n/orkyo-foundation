using Api.Models;
using Api.Security.Features;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMembershipEnricherTests
{
    [Fact]
    public async Task PassThrough_ReturnsInputUnchanged()
    {
        var enricher = new PassThroughTenantMembershipEnricher();
        var memberships = new List<TenantMembershipInfo>
        {
            new()
            {
                TenantId = Guid.NewGuid(),
                Slug = "acme",
                DisplayName = "Acme",
                Role = "admin",
                State = "suspended",
                IsOwner = true,
                IsTenantAdmin = true,
                Tier = "Community",
            },
        };

        var result = await enricher.EnrichAsync(memberships);

        result.Should().BeSameAs(memberships);
        result[0].CanReactivate.Should().BeNull("foundation has no suspension concept");
        result[0].SuspensionReason.Should().BeNull();
    }
}
