using Orkyo.Foundation.Seed.Scales;
using Xunit;

namespace Orkyo.Foundation.Seed.Tests;

public class ScaleCatalogTests
{
    [Theory]
    [InlineData("tiny")]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("xlarge")]
    public void Resolve_ReturnsScale_ForKnownSlug(string slug)
    {
        var scale = ScaleCatalog.Resolve(slug);
        Assert.Equal(slug, scale.Slug);
    }

    [Theory]
    [InlineData("TINY")]
    [InlineData("Medium")]
    [InlineData("XLARGE")]
    public void Resolve_IsCaseInsensitive(string slug)
    {
        var scale = ScaleCatalog.Resolve(slug);
        Assert.NotNull(scale);
    }

    [Fact]
    public void Resolve_Throws_ForUnknownSlug()
    {
        Assert.Throws<ArgumentException>(() => ScaleCatalog.Resolve("mega"));
    }

    [Fact]
    public void ScalesAreStrictlyOrdered_ByRequestCount()
    {
        var tiny = ScaleCatalog.Resolve("tiny");
        var small = ScaleCatalog.Resolve("small");
        var medium = ScaleCatalog.Resolve("medium");
        var large = ScaleCatalog.Resolve("large");
        var xlarge = ScaleCatalog.Resolve("xlarge");

        Assert.True(tiny.Requests < small.Requests);
        Assert.True(small.Requests < medium.Requests);
        Assert.True(medium.Requests < large.Requests);
        Assert.True(large.Requests < xlarge.Requests);
    }

    [Theory]
    [InlineData("tiny")]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("xlarge")]
    public void AllScales_HavePositiveCounts(string slug)
    {
        var s = ScaleCatalog.Resolve(slug);
        Assert.True(s.Sites > 0, $"{slug}.Sites must be > 0");
        Assert.True(s.SpacesPerSite > 0, $"{slug}.SpacesPerSite must be > 0");
        Assert.True(s.People > 0, $"{slug}.People must be > 0");
        Assert.True(s.Departments > 0, $"{slug}.Departments must be > 0");
        Assert.True(s.JobTitles > 0, $"{slug}.JobTitles must be > 0");
        Assert.True(s.Requests > 0, $"{slug}.Requests must be > 0");
        Assert.True(s.TimeWindowDays > 0, $"{slug}.TimeWindowDays must be > 0");
    }
}
