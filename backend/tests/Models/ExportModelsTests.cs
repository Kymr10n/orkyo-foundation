using Api.Models.Export;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers the 0%-coverage Export model types in <c>Models/Export/ExportPayload.cs</c>.
/// </summary>
public class ExportModelsTests
{
    // ── ExportRequest ──────────────────────────────────────────────────────

    [Fact]
    public void ExportRequest_Defaults_IncludeMasterDataTrue_PlanningDataFalse()
    {
        var req = new ExportRequest();

        req.IncludeMasterData.Should().BeTrue();
        req.IncludePlanningData.Should().BeFalse();
        req.SiteIds.Should().BeNull();
    }

    // ── ExportPayload / ExportProvenance / ExportData ──────────────────────

    [Fact]
    public void ExportData_AllCollections_NullByDefault()
    {
        var data = new ExportData();

        data.Sites.Should().BeNull();
        data.Criteria.Should().BeNull();
        data.SpaceGroups.Should().BeNull();
        data.Templates.Should().BeNull();
        data.Requests.Should().BeNull();
    }

    // ── ExportSite / ExportSpace / ExportCapability ────────────────────────

    [Fact]
    public void ExportSpace_Defaults_EmptyLists()
    {
        var space = new ExportSpace { Name = "Hall A", IsPhysical = true };

        space.Capabilities.Should().BeNull();
        space.Geometry.Should().BeNull();
        space.Properties.Should().BeNull();
        space.GroupKey.Should().BeNull();
    }

    // ── ExportTemplate / ExportTemplateItem ───────────────────────────────

    [Fact]
    public void ExportTemplate_Defaults_FixedFlagsAreFalse()
    {
        var template = new ExportTemplate
        {
            Key = "t",
            Name = "Test",
            EntityType = "space"
        };

        template.FixedStart.Should().BeFalse();
        template.FixedEnd.Should().BeFalse();
        template.FixedDuration.Should().BeFalse();
        template.Items.Should().BeEmpty();
    }
}
