using System.Reflection;
using Api.Models.Preset;
using Api.Services;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Services;

public class PresetTemplateLoaderTests
{
    [Fact]
    public void LoadPreset_ShouldThrowArgumentException_ForUnknownTemplateKey()
    {
        var act = () => PresetTemplateLoader.LoadPreset(
            "unknown",
            Path.GetTempPath(),
            typeof(PresetTemplateLoaderTests).Assembly);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*No preset file for template: unknown*");
    }

    [Fact]
    public void LoadPreset_ShouldLoadPresetFromFileSystem_WhenFileExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"preset-loader-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var filePath = Path.Combine(tempRoot, "camping-site.preset.json");
            File.WriteAllText(filePath, """
{
  "presetId": "camping-site-v1",
  "name": "Camping",
  "version": "1.0.0",
  "createdAt": "2026-01-01T00:00:00Z",
  "contents": {
    "criteria": [],
    "spaceGroups": [],
    "templates": {
      "space": [],
      "group": [],
      "request": []
    }
  }
}
""");

            var preset = PresetTemplateLoader.LoadPreset(
                StarterTemplateCatalog.CampingSite,
                tempRoot,
                typeof(PresetTemplateLoaderTests).Assembly);

            preset.PresetId.Should().Be("camping-site-v1");
            preset.Name.Should().Be("Camping");
            preset.Version.Should().Be("1.0.0");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadPreset_ShouldThrowFileNotFound_WhenFileAndResourceAreMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"preset-loader-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var act = () => PresetTemplateLoader.LoadPreset(
                StarterTemplateCatalog.Manufacturing,
                tempRoot,
                typeof(PresetTemplateLoaderTests).Assembly);

            act.Should().Throw<FileNotFoundException>()
                .WithMessage("*manufacturing-ch.preset.json*");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
