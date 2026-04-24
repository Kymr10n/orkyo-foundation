using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orkyo.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Orkyo.Saas.Tests.Services;

public sealed class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _basePath;

    public LocalFileStorageServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "orkyo-filestorage-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
            Directory.Delete(_basePath, recursive: true);
    }

    [Fact]
    public void Constructor_ShouldCreateBaseDirectory_WhenMissing()
    {
        Directory.Exists(_basePath).Should().BeFalse("precondition: temp path must not exist yet");

        _ = CreateService();

        Directory.Exists(_basePath).Should().BeTrue();
    }

    [Fact]
    public async Task GetFloorplanAsync_ShouldReturnNull_WhenFileMissing()
    {
        var service = CreateService();

        var result = await service.GetFloorplanAsync("tenant_0000/floorplans/does-not-exist.png");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetFloorplanAsync_ShouldReturnPngMime_WhenExtensionIsPng()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        await SeedFloorplanAsync(tenantId, "floor-01.png", SynthesizePng(4, 4));

        var result = await service.GetFloorplanAsync(FloorplanStoragePathPolicy.BuildRelativePath(tenantId, "floor-01.png"));

        result.Should().NotBeNull();
        result!.Value.mimeType.Should().Be("image/png");
        await result.Value.stream.DisposeAsync();
    }

    [Fact]
    public async Task GetFloorplanAsync_ShouldReturnJpegMime_WhenExtensionIsJpg()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        await SeedFloorplanAsync(tenantId, "floor-02.jpg", SynthesizePng(4, 4)); // bytes irrelevant; MIME derived from extension

        var result = await service.GetFloorplanAsync(FloorplanStoragePathPolicy.BuildRelativePath(tenantId, "floor-02.jpg"));

        result.Should().NotBeNull();
        result!.Value.mimeType.Should().Be("image/jpeg");
        await result.Value.stream.DisposeAsync();
    }

    [Fact]
    public async Task DeleteFloorplanAsync_ShouldNotThrow_WhenFileMissing()
    {
        var service = CreateService();

        var act = async () => await service.DeleteFloorplanAsync("tenant_0000/floorplans/does-not-exist.png");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteFloorplanAsync_ShouldRemoveFile_WhenPresent()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var (relativePath, fullPath) = await SeedFloorplanAsync(tenantId, "floor-del.png", SynthesizePng(2, 2));
        File.Exists(fullPath).Should().BeTrue("precondition");

        await service.DeleteFloorplanAsync(relativePath);

        File.Exists(fullPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFloorplanAsync_ShouldThrow_WhenPathEscapesBasePath()
    {
        var service = CreateService();

        var act = async () => await service.DeleteFloorplanAsync("../escape.png");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetImageDimensionsAsync_ShouldThrowArgumentException_OnNonImageStream()
    {
        var service = CreateService();
        await using var garbage = new MemoryStream(new byte[] { 0, 1, 2, 3, 4, 5 });

        var act = async () => await service.GetImageDimensionsAsync(garbage);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetImageDimensionsAsync_ShouldReturnSize_ForValidPng()
    {
        var service = CreateService();
        var bytes = SynthesizePng(width: 7, height: 11);
        await using var stream = new MemoryStream(bytes);

        var (w, h) = await service.GetImageDimensionsAsync(stream);

        w.Should().Be(7);
        h.Should().Be(11);
    }

    [Fact]
    public async Task SaveFloorplanAsync_ShouldPersistFile_AndReturnMetadata_ForValidPng()
    {
        var service = CreateService();
        var siteId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var bytes = SynthesizePng(width: 5, height: 9);
        var form = BuildFormFile(bytes, "plan.png", "image/png");

        var (relativePath, mime, w, h) = await service.SaveFloorplanAsync(form, siteId, tenantId);

        mime.Should().Be("image/png");
        w.Should().Be(5);
        h.Should().Be(9);
        relativePath.Should().StartWith($"tenant_{tenantId}/floorplans/{siteId}_");
        File.Exists(Path.Combine(_basePath, relativePath)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFloorplanAsync_ShouldThrow_OnEmptyFile()
    {
        var service = CreateService();
        var form = BuildFormFile(Array.Empty<byte>(), "empty.png", "image/png");

        var act = async () => await service.SaveFloorplanAsync(form, Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SaveFloorplanAsync_ShouldThrow_OnNonImageBytes()
    {
        var service = CreateService();
        var form = BuildFormFile(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, "not-an-image.bin", "application/octet-stream");

        var act = async () => await service.SaveFloorplanAsync(form, Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private LocalFileStorageService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.FileStoragePath] = _basePath,
            })
            .Build();

        var settings = Mock.Of<ITenantSettingsService>(s => s.GetSettingsAsync() == Task.FromResult(new TenantSettings
        {
            Upload_MaxFileSizeMb = 10,
            Upload_AllowedMimeTypes = "image/png,image/jpeg,image/jpg",
        }));

        return new LocalFileStorageService(config, NullLogger<LocalFileStorageService>.Instance, settings);
    }

    private async Task<(string relativePath, string fullPath)> SeedFloorplanAsync(Guid tenantId, string fileName, byte[] bytes)
    {
        var directory = FloorplanStoragePathPolicy.BuildTenantFloorplanDirectory(_basePath, tenantId);
        Directory.CreateDirectory(directory);
        var fullPath = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes);
        return (FloorplanStoragePathPolicy.BuildRelativePath(tenantId, fileName), fullPath);
    }

    private static IFormFile BuildFormFile(byte[] bytes, string fileName, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, name: "file", fileName: fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private static byte[] SynthesizePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(85, 107, 47, 255));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
