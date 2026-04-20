using Api.Models;
using Microsoft.AspNetCore.Http;
using Moq;
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
            ImagePath = "uploads/site-1.png",
            MimeType = "image/png",
            FileSizeBytes = 1024,
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
            ImagePath = "uploads/site-2.webp",
            MimeType = "image/webp",
            FileSizeBytes = 2048,
            WidthPx = 1920,
            HeightPx = 1080,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = null
        };

        Assert.Equal("uploads/site-2.webp", meta.ImagePath);
        Assert.Null(meta.UploadedByUserId);
    }

    [Fact]
    public void UploadFloorplanRequest_StoresProvidedFile()
    {
        var formFile = new Mock<IFormFile>();
        var request = new UploadFloorplanRequest
        {
            File = formFile.Object
        };

        Assert.Same(formFile.Object, request.File);
    }
}
