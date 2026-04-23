using Api.Models;
using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class OrgContextExtensionsTests
{
    private static TenantContext MakeTenant() => new()
    {
        TenantId = Guid.NewGuid(),
        TenantSlug = "acme",
        TenantDbConnectionString = "Host=db;Database=acme_tenant;",
        Tier = ServiceTier.Free,
        Status = TenantStatusConstants.Active
    };

    [Fact]
    public void FromTenant_ShouldMapAllFieldsCorrectly()
    {
        var tenant = MakeTenant();

        var org = OrgContextExtensions.FromTenant(tenant);

        org.OrgId.Should().Be(tenant.TenantId);
        org.OrgSlug.Should().Be(tenant.TenantSlug);
        org.DbConnectionString.Should().Be(tenant.TenantDbConnectionString);
    }

    [Fact]
    public void FromTenant_ShouldNotShareReferenceWithTenantContext()
    {
        var tenant = MakeTenant();

        var org1 = OrgContextExtensions.FromTenant(tenant);
        var org2 = OrgContextExtensions.FromTenant(tenant);

        org1.Should().NotBeSameAs(org2);
        org1.OrgId.Should().Be(org2.OrgId);
    }
}
