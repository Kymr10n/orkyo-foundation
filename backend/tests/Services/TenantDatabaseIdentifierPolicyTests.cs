using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantDatabaseIdentifierPolicyTests
{
    [Fact]
    public void BuildFromSlug_ShouldPrefixTenant()
    {
        var result = TenantDatabaseIdentifierPolicy.BuildFromSlug("acme");

        result.Should().Be("tenant_acme");
    }

    [Fact]
    public void BuildFromSlug_ShouldReplaceHyphensWithUnderscores()
    {
        var result = TenantDatabaseIdentifierPolicy.BuildFromSlug("north-west-team");

        result.Should().Be("tenant_north_west_team");
    }

    [Fact]
    public void BuildFromSlug_ShouldPreserveNumbers()
    {
        var result = TenantDatabaseIdentifierPolicy.BuildFromSlug("team9-zone2");

        result.Should().Be("tenant_team9_zone2");
    }
}
