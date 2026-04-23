using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class SiteFloorplanQueryContractTests
{
    [Fact]
    public void BuildUpdateFloorplanSql_TargetsSitesTableAndFiltersBySiteId()
    {
        var sql = SiteFloorplanQueryContract.BuildUpdateFloorplanSql();

        sql.Should().Contain("UPDATE sites SET");
        sql.Should().Contain("WHERE id = @siteId");
    }

    [Fact]
    public void BuildUpdateFloorplanSql_WritesAllSixFloorplanColumnsAndStampsUploadedAtServerSide()
    {
        var sql = SiteFloorplanQueryContract.BuildUpdateFloorplanSql();

        sql.Should().Contain("floorplan_image_path = @path");
        sql.Should().Contain("floorplan_mime_type = @mime");
        sql.Should().Contain("floorplan_file_size_bytes = @size");
        sql.Should().Contain("floorplan_width_px = @w");
        sql.Should().Contain("floorplan_height_px = @h");
        sql.Should().Contain("floorplan_uploaded_at = NOW()");
    }

    [Fact]
    public void BuildUpdateFloorplanSql_DoesNotParameterizeUploadedAt()
    {
        // floorplan_uploaded_at must be server-side NOW() so all deployments
        // (regardless of caller clock skew) record a consistent server timestamp.
        SiteFloorplanQueryContract.BuildUpdateFloorplanSql()
            .Should().NotContain("floorplan_uploaded_at = @");
    }

    [Fact]
    public void ParameterNames_AreStable()
    {
        SiteFloorplanQueryContract.ImagePathParameterName.Should().Be("path");
        SiteFloorplanQueryContract.MimeTypeParameterName.Should().Be("mime");
        SiteFloorplanQueryContract.FileSizeBytesParameterName.Should().Be("size");
        SiteFloorplanQueryContract.WidthPxParameterName.Should().Be("w");
        SiteFloorplanQueryContract.HeightPxParameterName.Should().Be("h");
        SiteFloorplanQueryContract.SiteIdParameterName.Should().Be("siteId");
    }
}

public class SiteFloorplanCommandFactoryTests
{
    [Fact]
    public void CreateUpdateFloorplanCommand_BindsAllSixParameters()
    {
        var siteId = Guid.NewGuid();

        using var cmd = SiteFloorplanCommandFactory.CreateUpdateFloorplanCommand(
            connection: null!,
            siteId: siteId,
            imagePath: "uploads/floorplans/abc.png",
            mimeType: "image/png",
            fileSizeBytes: 12345L,
            widthPx: 1920,
            heightPx: 1080);

        cmd.Parameters.Should().HaveCount(6);
        cmd.Parameters["path"].Value.Should().Be("uploads/floorplans/abc.png");
        cmd.Parameters["mime"].Value.Should().Be("image/png");
        cmd.Parameters["size"].Value.Should().Be(12345L);
        cmd.Parameters["w"].Value.Should().Be(1920);
        cmd.Parameters["h"].Value.Should().Be(1080);
        cmd.Parameters["siteId"].Value.Should().Be(siteId);
    }

    [Fact]
    public void CreateUpdateFloorplanCommand_AllowsNonPngMimeTypes()
    {
        // Foundation read-side FloorplanMetadata.MimeType is mime-agnostic;
        // the writer must not collapse to a hardcoded image/png literal so that
        // future upload endpoints (e.g. webp/jpeg) can reuse this command.
        using var cmd = SiteFloorplanCommandFactory.CreateUpdateFloorplanCommand(
            connection: null!,
            siteId: Guid.NewGuid(),
            imagePath: "uploads/x.webp",
            mimeType: "image/webp",
            fileSizeBytes: 1L,
            widthPx: 1,
            heightPx: 1);

        cmd.Parameters["mime"].Value.Should().Be("image/webp");
    }
}
