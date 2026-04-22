using Api.Models.Preset;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Models;

public class StarterTemplateCatalogTests
{
    [Fact]
    public void All_ShouldExposeExpectedStableTemplateKeysInOrder()
    {
        StarterTemplateCatalog.All.Select(t => t.Key).Should().Equal(
            StarterTemplateCatalog.Empty,
            StarterTemplateCatalog.Demo,
            StarterTemplateCatalog.CampingSite,
            StarterTemplateCatalog.ConstructionSite,
            StarterTemplateCatalog.Manufacturing);
    }

    [Fact]
    public void All_ShouldContainExactlyOneDemoTemplate()
    {
        StarterTemplateCatalog.All.Should().ContainSingle(t => t.Key == StarterTemplateCatalog.Demo && t.IncludesDemoData);
    }

    [Theory]
    [InlineData(StarterTemplateCatalog.Empty, true)]
    [InlineData(StarterTemplateCatalog.Demo, true)]
    [InlineData(StarterTemplateCatalog.CampingSite, true)]
    [InlineData(StarterTemplateCatalog.ConstructionSite, true)]
    [InlineData(StarterTemplateCatalog.Manufacturing, true)]
    [InlineData("bogus", false)]
    public void IsKnown_ShouldRecognizeConfiguredKeys(string key, bool expected)
    {
        StarterTemplateCatalog.IsKnown(key).Should().Be(expected);
    }

    [Theory]
    [InlineData(StarterTemplateCatalog.Demo, true)]
    [InlineData(StarterTemplateCatalog.Empty, false)]
    [InlineData(StarterTemplateCatalog.CampingSite, false)]
    public void IsDemoTemplate_ShouldOnlyMatchDemo(string key, bool expected)
    {
        StarterTemplateCatalog.IsDemoTemplate(key).Should().Be(expected);
    }

    [Theory]
    [InlineData(StarterTemplateCatalog.Empty, false)]
    [InlineData(StarterTemplateCatalog.Demo, false)]
    [InlineData(StarterTemplateCatalog.CampingSite, true)]
    [InlineData(StarterTemplateCatalog.ConstructionSite, true)]
    [InlineData(StarterTemplateCatalog.Manufacturing, true)]
    public void IsPresetTemplate_ShouldMatchOnlyPresetBackedTemplates(string key, bool expected)
    {
        StarterTemplateCatalog.IsPresetTemplate(key).Should().Be(expected);
    }

    [Theory]
    [InlineData(StarterTemplateCatalog.CampingSite, "camping-site.preset.json")]
    [InlineData(StarterTemplateCatalog.ConstructionSite, "construction-site.preset.json")]
    [InlineData(StarterTemplateCatalog.Manufacturing, "manufacturing-ch.preset.json")]
    public void TryGetPresetFileName_ShouldReturnMappedFileName(string key, string expectedFileName)
    {
        var found = StarterTemplateCatalog.TryGetPresetFileName(key, out var fileName);

        found.Should().BeTrue();
        fileName.Should().Be(expectedFileName);
    }

    [Theory]
    [InlineData(StarterTemplateCatalog.Empty)]
    [InlineData(StarterTemplateCatalog.Demo)]
    [InlineData("bogus")]
    public void TryGetPresetFileName_ShouldReturnFalse_WhenTemplateIsNotPresetBacked(string key)
    {
        var found = StarterTemplateCatalog.TryGetPresetFileName(key, out var fileName);

        found.Should().BeFalse();
        fileName.Should().BeNull();
    }
}
