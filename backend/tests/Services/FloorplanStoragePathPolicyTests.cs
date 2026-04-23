using Api.Services;
using FluentAssertions;

namespace Orkyo.Foundation.Tests.Services;

public class FloorplanStoragePathPolicyTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UniqueId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void TenantDirectoryPrefix_IsLocked()
    {
        FloorplanStoragePathPolicy.TenantDirectoryPrefix.Should().Be("tenant_");
    }

    [Fact]
    public void FloorplanSubdirectory_IsLocked()
    {
        FloorplanStoragePathPolicy.FloorplanSubdirectory.Should().Be("floorplans");
    }

    [Fact]
    public void BuildRelativePath_UsesTenantPrefixedFloorplansSubdirectory()
    {
        var path = FloorplanStoragePathPolicy.BuildRelativePath(TenantId, "x.png");

        path.Should().Be(Path.Combine($"tenant_{TenantId}", "floorplans", "x.png"));
    }

    [Fact]
    public void BuildTenantFloorplanDirectory_PrependsBasePath()
    {
        var basePath = Path.Combine("var", "data");

        var path = FloorplanStoragePathPolicy.BuildTenantFloorplanDirectory(basePath, TenantId);

        path.Should().Be(Path.Combine(basePath, $"tenant_{TenantId}", "floorplans"));
    }

    [Fact]
    public void BuildFileName_ComposesSiteIdUnderscoreUniqueIdExtension()
    {
        var name = FloorplanStoragePathPolicy.BuildFileName(SiteId, UniqueId, ".png");

        name.Should().Be($"{SiteId}_{UniqueId}.png");
    }

    [Fact]
    public void BuildFileName_PreservesArbitraryExtension()
    {
        FloorplanStoragePathPolicy.BuildFileName(SiteId, UniqueId, ".jpg")
            .Should().EndWith(".jpg");
    }

    [Fact]
    public void BuildRelativePath_RoundTripsThroughBuildTenantFloorplanDirectory()
    {
        var basePath = Path.Combine("var", "data");
        var fileName = FloorplanStoragePathPolicy.BuildFileName(SiteId, UniqueId, ".png");

        var relative = FloorplanStoragePathPolicy.BuildRelativePath(TenantId, fileName);
        var full = Path.Combine(FloorplanStoragePathPolicy.BuildTenantFloorplanDirectory(basePath, TenantId), fileName);

        Path.Combine(basePath, relative).Should().Be(full);
    }
}
