using Api.Models;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Api.Tests.Models;

public class SiteModelsTests
{
    [Fact]
    public void SiteInfo_CanStoreAllFields()
    {
        var siteId = Guid.NewGuid();
        var created = DateTime.UtcNow;
        var floorplan = new FloorplanMetadata
        {
            AssetId = Guid.NewGuid(),
            FileName = "site-1.png",
            MimeType = "image/png",
            FileSizeBytes = 1024,
            ChecksumSha256 = new string('a', 64),
            WidthPx = 1200,
            HeightPx = 800,
            UploadedAt = created,
            UploadedByUserId = Guid.NewGuid()
        };

        var site = new SiteInfo
        {
            Id = siteId,
            Name = "HQ",
            Code = "HQ-1",
            Description = "Main office",
            Address = "123 Main St",
            Attributes = "{\"region\":\"EU\"}",
            Floorplan = floorplan,
            CreatedAt = created,
            UpdatedAt = created
        };

        Assert.Equal(siteId, site.Id);
        Assert.Equal("HQ", site.Name);
        Assert.Equal("HQ-1", site.Code);
        Assert.Equal("Main office", site.Description);
        Assert.Equal("123 Main St", site.Address);
        Assert.Equal("{\"region\":\"EU\"}", site.Attributes);
        Assert.NotNull(site.Floorplan);
        Assert.Equal("image/png", site.Floorplan!.MimeType);
    }

    [Fact]
    public void FloorplanMetadata_CanBeCreatedWithoutUploader()
    {
        var meta = new FloorplanMetadata
        {
            AssetId = Guid.NewGuid(),
            FileName = "site-2.jpg",
            MimeType = "image/jpeg",
            FileSizeBytes = 2048,
            ChecksumSha256 = new string('b', 64),
            WidthPx = 1920,
            HeightPx = 1080,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = null
        };

        Assert.Equal("site-2.jpg", meta.FileName);
        Assert.Null(meta.UploadedByUserId);
    }

    [Fact]
    public void UploadFloorplanRequest_StoresProvidedFile()
    {
        var content = new MemoryStream();
        var request = new UploadFloorplanRequest
        {
            Content = content,
            FileName = "test.png",
            ContentType = "image/png"
        };

        Assert.Same(content, request.Content);
        Assert.Equal("test.png", request.FileName);
        Assert.Equal("image/png", request.ContentType);
    }
}
