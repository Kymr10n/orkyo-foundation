using Orkyo.Foundation.Seed.Profiles;

namespace Orkyo.Foundation.Tests.Seeding;

public class ProfileCatalogTests
{
    [Theory]
    [InlineData("manufacturing")]
    [InlineData("Manufacturing")]
    public void Resolve_ReturnsManufacturing_CaseInsensitively(string slug)
    {
        var profile = ProfileCatalog.Resolve(slug);
        Assert.Equal("manufacturing", profile.Slug);
    }

    [Theory]
    [InlineData("fantasy")]
    [InlineData("camping")] // retired with the random seed path
    public void Resolve_Throws_ForUnknownProfile_NamingTheValidOne(string slug)
    {
        var ex = Assert.Throws<ArgumentException>(() => ProfileCatalog.Resolve(slug));
        Assert.Contains(slug, ex.Message);
        Assert.Contains("manufacturing", ex.Message);
    }

    [Fact]
    public void Manufacturing_HasNonEmptyNamePools()
    {
        var p = ProfileCatalog.Resolve("manufacturing");
        Assert.NotEmpty(p.JobTitlePool);
        Assert.NotEmpty(p.DepartmentRootPool);
        Assert.NotEmpty(p.DepartmentChildPool);
    }
}
