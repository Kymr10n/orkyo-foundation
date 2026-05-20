using Orkyo.Foundation.Seed.Profiles;
using Xunit;

namespace Orkyo.Foundation.Seed.Tests;

public class ProfileCatalogTests
{
    [Theory]
    [InlineData("generic")]
    [InlineData("manufacturing")]
    [InlineData("construction")]
    [InlineData("camping")]
    [InlineData("education")]
    public void Resolve_ReturnsProfile_ForKnownSlug(string slug)
    {
        var profile = ProfileCatalog.Resolve(slug);
        Assert.Equal(slug, profile.Slug);
    }

    [Theory]
    [InlineData("GENERIC")]
    [InlineData("Manufacturing")]
    public void Resolve_IsCaseInsensitive(string slug)
    {
        var profile = ProfileCatalog.Resolve(slug);
        Assert.NotNull(profile);
    }

    [Fact]
    public void Resolve_Throws_ForUnknownProfile()
    {
        Assert.Throws<ArgumentException>(() => ProfileCatalog.Resolve("fantasy"));
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("manufacturing")]
    [InlineData("construction")]
    [InlineData("camping")]
    [InlineData("education")]
    public void AllProfiles_HaveNonEmptyNamePools(string slug)
    {
        var p = ProfileCatalog.Resolve(slug);
        Assert.NotEmpty(p.SiteNamePool);
        Assert.NotEmpty(p.SpaceNameTemplate);
        Assert.NotEmpty(p.DepartmentRootPool);
        Assert.NotEmpty(p.JobTitlePool);
        Assert.NotEmpty(p.RequestNameVerbs);
        Assert.NotEmpty(p.RequestNameNouns);
    }
}
