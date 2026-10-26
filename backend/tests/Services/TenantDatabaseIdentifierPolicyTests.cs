using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantDatabaseIdentifierPolicyTests
{
    [Theory]
    [InlineData("acme", "tenant_acme")]
    [InlineData("north-west-team", "tenant_north_west_team")]   // hyphens become underscores
    [InlineData("team9-zone2", "tenant_team9_zone2")]           // digits are kept
    public void BuildFromSlug_PrefixesAndNormalisesTheSlug(string slug, string expected) =>
        TenantDatabaseIdentifierPolicy.BuildFromSlug(slug).Should().Be(expected);
}
