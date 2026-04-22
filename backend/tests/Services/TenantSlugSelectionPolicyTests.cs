using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantSlugSelectionPolicyTests
{
    [Fact]
    public void SelectSlug_ShouldPreferSubdomain_WhenBothProvided()
    {
        var slug = TenantSlugSelectionPolicy.SelectSlug("subdomain-tenant", "header-tenant");

        slug.Should().Be("subdomain-tenant");
    }

    [Fact]
    public void SelectSlug_ShouldUseHeader_WhenSubdomainMissing()
    {
        var slug = TenantSlugSelectionPolicy.SelectSlug(null, "header-tenant");

        slug.Should().Be("header-tenant");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, "")]
    [InlineData(null, "   ")]
    public void SelectSlug_ShouldReturnNull_WhenBothCandidatesBlank(string? subdomain, string? tenantHeader)
    {
        var slug = TenantSlugSelectionPolicy.SelectSlug(subdomain, tenantHeader);

        slug.Should().BeNull();
    }
}