using Api.Models;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class OrgContextTests
{
    [Fact]
    public void FromTenant_MapsIdCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var tenant = MakeTenant(tenantId: tenantId);

        var org = OrgContextExtensions.FromTenant(tenant);

        org.OrgId.Should().Be(tenantId);
    }

    [Fact]
    public void FromTenant_MapsSlugCorrectly()
    {
        var tenant = MakeTenant(slug: "acme-corp");

        var org = OrgContextExtensions.FromTenant(tenant);

        org.OrgSlug.Should().Be("acme-corp");
    }

    [Fact]
    public void FromTenant_MapsConnectionStringCorrectly()
    {
        var connStr = "Host=db.example.com;Database=tenant_acme;Username=admin;Password=secret";
        var tenant = MakeTenant(connectionString: connStr);

        var org = OrgContextExtensions.FromTenant(tenant);

        org.DbConnectionString.Should().Be(connStr);
    }

    [Fact]
    public void FromTenant_DoesNotExposeTier()
    {
        var tenant = MakeTenant();

        var org = OrgContextExtensions.FromTenant(tenant);

        // OrgContext should not have a Tier property — compile-time guarantee.
        org.Should().BeOfType<OrgContext>();
        typeof(OrgContext).GetProperty("Tier").Should().BeNull();
    }

    [Fact]
    public void FromTenant_DoesNotExposeStatus()
    {
        typeof(OrgContext).GetProperty("Status").Should().BeNull();
    }

    private static TenantContext MakeTenant(
        Guid? tenantId = null,
        string slug = "test",
        string connectionString = "Host=localhost;Database=test") => new()
        {
            TenantId = tenantId ?? Guid.NewGuid(),
            TenantSlug = slug,
            TenantDbConnectionString = connectionString,
            Tier = ServiceTier.Free,
            Status = "active"
        };
}
