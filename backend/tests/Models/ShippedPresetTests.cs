using Api.Constants;
using Api.Models.Preset;
using Api.Services;
using Api.Validators;

namespace Api.Tests.Models;

/// <summary>
/// The two starter presets ship embedded in Orkyo.Foundation.Core and are applied at first start
/// with no admin in the loop, so a broken file is an install that silently starts empty. These
/// pin, without a database: the file is in the assembly manifest, it deserialises, it passes its
/// own validator, every cross-reference resolves, and every catalog key it names is real.
/// </summary>
public class ShippedPresetTests
{
    public static TheoryData<string, string> Shipped => new()
    {
        { StarterTemplateCatalog.Manufacturing, "manufacturing-workshop-v1" },
        { StarterTemplateCatalog.Office, "office-v1" },
    };

    private static Preset Load(string key) =>
        PresetTemplateLoader.LoadPreset(
            key,
            Path.Combine(Path.GetTempPath(), "no-such-presets-dir"),
            typeof(PresetApplier).Assembly);

    [Theory]
    [MemberData(nameof(Shipped))]
    public void IsEmbeddedAndLoads(string key, string expectedPresetId)
    {
        var preset = Load(key);

        preset.PresetId.Should().Be(expectedPresetId, "the Community smoke and the once-only skip look this id up");
        preset.Version.Should().Be(PresetValidator.CurrentVersion);
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void PassesItsOwnValidator(string key, string _)
    {
        var result = new PresetValidator().Validate(Load(key));

        result.Errors.Select(e => e.ErrorMessage).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void CatalogKeysAreRealCatalogEntries(string key, string _)
    {
        // A type without display names is meant to come from the catalog; a typo here would
        // pass validation as "ad-hoc" only to fail on the missing names, so pin the intent.
        var catalogKeys = Load(key).Contents.ResourceTypes
            .Where(t => t.DisplayName == null)
            .Select(t => t.Key);

        catalogKeys.Should().OnlyContain(k => ResourceTypeCatalog.Find(k) != null);
    }

    [Fact]
    public void Manufacturing_IsASmallWorkshop()
    {
        var contents = Load(StarterTemplateCatalog.Manufacturing).Contents;

        contents.ResourceTypes.Select(t => t.Key).Should().BeEquivalentTo("room", "person", "tool", "mill", "drill");
        contents.Resources.Should().HaveCount(11);
        contents.Resources.Where(r => r.TypeKey == "room").Should().HaveCount(3);
        contents.Resources.Where(r => r.TypeKey == "person").Should().HaveCount(4);
        contents.SpaceGroups.Should().OnlyContain(g => g.ResourceTypeKey == "room");
    }

    [Fact]
    public void Office_MatchesTheRetiredCommunitySeed_SoUpgradedInstallsAdoptItsRows()
    {
        // The Community migrator used to insert exactly these four spaces; the applier adopts
        // by code within the type, so the names and codes here must stay what that seed wrote.
        var contents = Load(StarterTemplateCatalog.Office).Contents;

        contents.ResourceTypes.Select(t => t.Key).Should().Equal(ResourceTypeKeys.Space);
        contents.Resources.Select(r => (r.Name, r.Code)).Should().BeEquivalentTo(new[]
        {
            ("Meeting Room A", "MR-A"),
            ("Meeting Room B", "MR-B"),
            ("Open Workspace", "OW-1"),
            ("Focus Booth", "FB-1"),
        });
    }
}
