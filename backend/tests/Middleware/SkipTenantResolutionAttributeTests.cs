using Api.Middleware;
using Api.Services;
using Microsoft.AspNetCore.Http;

namespace Orkyo.Foundation.Tests.Middleware;

public class SkipTenantResolutionAttributeTests
{
    [Fact]
    public void SkipTenantResolutionAttribute_ImplementsInterface()
    {
        var attr = new SkipTenantResolutionAttribute();
        attr.Should().BeAssignableTo<ISkipTenantResolution>();
    }

    [Fact]
    public void SkipTenantResolutionAttribute_IsAttribute()
    {
        var attr = new SkipTenantResolutionAttribute();
        attr.Should().BeAssignableTo<Attribute>();
    }

    [Fact]
    public void SkipTenantResolutionAttribute_AllowsMultiple_IsConsistentWithDeclaration()
    {
        var usage = typeof(SkipTenantResolutionAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void SkipTenantResolutionAttribute_ValidTargets_IncludeClassAndMethod()
    {
        var usage = typeof(SkipTenantResolutionAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Class);
        usage.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }
}

public class TenantContextAccessExtensionsTests
{
    // ── GetTenantContext ───────────────────────────────────────────────────

    [Fact]
    public void GetTenantContext_WhenSet_ReturnsTenantContext()
    {
        var ctx = new DefaultHttpContext();
        var tenantContext = new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            TenantDbConnectionString = "Host=localhost;Database=acme",
            Status = "active"
        };
        ctx.Items["TenantContext"] = tenantContext;

        var result = ctx.GetTenantContext();

        result.Should().BeSameAs(tenantContext);
    }

    [Fact]
    public void GetTenantContext_WhenNotSet_ThrowsInvalidOperation()
    {
        var ctx = new DefaultHttpContext();

        var act = () => ctx.GetTenantContext();

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("Tenant context not available");
    }

    // ── GetOrgContext ──────────────────────────────────────────────────────

    [Fact]
    public void GetOrgContext_WhenSet_ReturnsOrgContext()
    {
        var ctx = new DefaultHttpContext();
        var orgContext = new OrgContext
        {
            OrgId = Guid.NewGuid(),
            OrgSlug = "acme",
            DbConnectionString = "Host=localhost;Database=acme_org"
        };
        ctx.Items["OrgContext"] = orgContext;

        var result = ctx.GetOrgContext();

        result.Should().BeSameAs(orgContext);
    }

    [Fact]
    public void GetOrgContext_WhenNotSet_ThrowsInvalidOperation()
    {
        var ctx = new DefaultHttpContext();

        var act = () => ctx.GetOrgContext();

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("Org context not available");
    }
}
