using Api.Models;
using Api.Security;
using Api.Services;

namespace Orkyo.Foundation.Tests.Security;

public class CurrentTenantTests
{
    [Fact]
    public void HasTenant_WhenNoContextSet_ReturnsFalse() =>
        new CurrentTenant().HasTenant.Should().BeFalse();

    [Fact]
    public void TenantId_WhenNoContextSet_ReturnsEmptyGuid() =>
        new CurrentTenant().TenantId.Should().Be(Guid.Empty);

    [Fact]
    public void TenantSlug_WhenNoContextSet_ReturnsEmptyString() =>
        new CurrentTenant().TenantSlug.Should().BeEmpty();

    [Fact]
    public void RequireTenantId_WhenNoContextSet_ThrowsInvalidOperationException()
    {
        ((Action)(() => new CurrentTenant().RequireTenantId())).Should().Throw<InvalidOperationException>()
            .WithMessage("Tenant context required");
    }

    [Fact]
    public void RequireTenantId_WhenContextSet_ReturnsTenantId()
    {
        var tenant = new CurrentTenant();
        var tenantId = Guid.NewGuid();
        tenant.SetContext(new TenantContext
        {
            TenantId = tenantId,
            TenantSlug = "test-tenant",
            TenantDbConnectionString = "Host=localhost",
            Tier = ServiceTier.Professional,
            Status = "active"
        });

        tenant.RequireTenantId().Should().Be(tenantId);
    }

    [Fact]
    public void HasTenant_WhenContextSet_ReturnsTrue()
    {
        var tenant = new CurrentTenant();
        tenant.SetContext(new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            TenantDbConnectionString = "Host=localhost",
            Tier = ServiceTier.Enterprise,
            Status = "active"
        });

        tenant.HasTenant.Should().BeTrue();
    }

    [Fact]
    public void GetTenantContext_WhenContextSet_ReturnsContext()
    {
        var tenant = new CurrentTenant();
        var ctx = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            TenantDbConnectionString = "Host=localhost",
            Tier = ServiceTier.Enterprise,
            Status = "active"
        };
        tenant.SetContext(ctx);
        tenant.GetTenantContext().Should().BeSameAs(ctx);
    }

    [Fact]
    public void GetTenantContext_WhenNoContextSet_ReturnsNull() =>
        new CurrentTenant().GetTenantContext().Should().BeNull();
}
