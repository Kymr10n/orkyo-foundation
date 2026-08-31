using Api.Security;
using AwesomeAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Security;

/// <summary>
/// Scopes are what let an automated caller reuse the tenant role checks a human goes through.
/// The mapping is therefore load-bearing: get it wrong and a read-only token either writes or
/// cannot read.
/// </summary>
public class PlatformApiScopesTests
{
    [Fact]
    public void AWriteScopedToken_ActsAsAnEditor()
    {
        PlatformApiScopes.ScopeToRole(PlatformApiScopes.ScheduleWrite)
            .Should().Be(TenantRole.Editor);
    }

    [Fact]
    public void AReadOnlyToken_ActsAsAViewer()
    {
        PlatformApiScopes.ScopeToRole(PlatformApiScopes.ScheduleRead)
            .Should().Be(TenantRole.Viewer);
    }

    [Fact]
    public void WriteImpliesRead_SoBothScopesTogetherStillMeanEditor()
    {
        var both = PlatformApiScopes.Join([PlatformApiScopes.ScheduleRead, PlatformApiScopes.ScheduleWrite]);

        PlatformApiScopes.ScopeToRole(both).Should().Be(TenantRole.Editor);
    }

    [Fact]
    public void NoScopeMapsToAdmin()
    {
        // Administration is a human surface. If this ever fails, a token has gained the ability to
        // manage users, settings and quotas — which no v1 scope is meant to grant.
        PlatformApiScopes.All.Select(PlatformApiScopes.ScopeToRole)
            .Should().NotContain(TenantRole.Admin);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("something:else")]
    public void AnUnrecognisedScopeStringGrantsNothing(string scopes)
    {
        PlatformApiScopes.ScopeToRole(scopes).Should().Be(TenantRole.None);
    }

    [Fact]
    public void ScopesRoundTripThroughTheStoredSpaceDelimitedForm()
    {
        var joined = PlatformApiScopes.Join([PlatformApiScopes.ScheduleRead, PlatformApiScopes.ScheduleWrite]);

        joined.Should().Be("schedule:read schedule:write");
        PlatformApiScopes.Split(joined).Should().BeEquivalentTo(
            [PlatformApiScopes.ScheduleRead, PlatformApiScopes.ScheduleWrite]);
    }

    [Fact]
    public void SplitToleratesExtraWhitespace()
    {
        PlatformApiScopes.Split("  schedule:read   schedule:write  ")
            .Should().BeEquivalentTo([PlatformApiScopes.ScheduleRead, PlatformApiScopes.ScheduleWrite]);
    }

    [Fact]
    public void AreAllKnown_RejectsAScopeOutsideTheAllowList()
    {
        PlatformApiScopes.AreAllKnown([PlatformApiScopes.ScheduleRead, "tenant:admin"])
            .Should().BeFalse();
        PlatformApiScopes.AreAllKnown([PlatformApiScopes.ScheduleRead]).Should().BeTrue();
    }
}
