using Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Orkyo.Foundation.Tests.Services;

public class FileStorageServiceTests : IDisposable
{
    private readonly string _testStoragePath;
    private readonly LocalFileStorageService _service;

    public FileStorageServiceTests()
    {
        _testStoragePath = Path.Combine(Path.GetTempPath(), $"test_storage_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testStoragePath);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FILE_STORAGE_PATH"] = _testStoragePath
            })
            .Build();

        var loggerMock = new Mock<ILogger<LocalFileStorageService>>();
        var settingsServiceMock = new Mock<ITenantSettingsService>();
        settingsServiceMock.Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new Api.Models.TenantSettings());
        _service = new LocalFileStorageService(configuration, loggerMock.Object, settingsServiceMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testStoragePath))
            Directory.Delete(_testStoragePath, true);
    }

    [Fact]
    public async Task SaveFloorplanAsync_WithValidPngFile_ShouldSaveSuccessfully()
    {
        var siteId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var file = CreateMockFormFile("test.png", "image/png", CreateTestPngImage());

        var (filePath, detectedMimeType, widthPx, heightPx) = await _service.SaveFloorplanAsync(file, siteId, tenantId);

        filePath.Should().NotBeNullOrEmpty();
        filePath.Should().Contain($"tenant_{tenantId}");
        filePath.Should().Contain("floorplans");
        filePath.Should().EndWith(".png");
        detectedMimeType.Should().Be("image/png");
        widthPx.Should().Be(10);
        heightPx.Should().Be(10);
        File.Exists(Path.Combine(_testStoragePath, filePath)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFloorplanAsync_WithValidJpegFile_ShouldSaveSuccessfully()
    {
        var siteId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var file = CreateMockFormFile("test.jpg", "image/jpeg", CreateTestJpegImage());

        var (filePath, detectedMimeType, widthPx, heightPx) = await _service.SaveFloorplanAsync(file, siteId, tenantId);

        filePath.Should().NotBeNullOrEmpty();
        filePath.Should().EndWith(".jpg");
        detectedMimeType.Should().Be("image/jpeg");
        widthPx.Should().Be(10);
        heightPx.Should().Be(10);
        File.Exists(Path.Combine(_testStoragePath, filePath)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFloorplanAsync_WithEmptyFile_ShouldThrowArgumentException()
    {
        var file = CreateMockFormFile("empty.png", "image/png", Array.Empty<byte>());

        Func<Task> act = async () => await _service.SaveFloorplanAsync(file, Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("File is empty");
    }

    [Fact]
    public async Task SaveFloorplanAsync_WithTooLargeFile_ShouldThrowArgumentException()
    {
        var largeContent = new byte[11 * 1024 * 1024];
        var file = CreateMockFormFile("large.png", "image/png", largeContent);

        Func<Task> act = async () => await _service.SaveFloorplanAsync(file, Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("File size exceeds maximum of 10MB");
    }

    [Fact]
    public async Task SaveFloorplanAsync_WithInvalidMimeType_ShouldThrowArgumentException()
    {
        var file = CreateMockFormFile("test.gif", "image/gif", new byte[] { 1, 2, 3 });

        Func<Task> act = async () => await _service.SaveFloorplanAsync(file, Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not a*image*");
    }

    [Fact]
    public async Task GetFloorplanAsync_WithExistingFile_ShouldReturnStreamAndMimeType()
    {
        var file = CreateMockFormFile("test.png", "image/png", CreateTestPngImage());
        var (filePath, _, _, _) = await _service.SaveFloorplanAsync(file, Guid.NewGuid(), Guid.NewGuid());

        var result = await _service.GetFloorplanAsync(filePath);

        result.Should().NotBeNull();
        result!.Value.stream.Should().NotBeNull();
        result.Value.mimeType.Should().Be("image/png");
        result.Value.stream.CanRead.Should().BeTrue();
        result.Value.stream.Length.Should().BeGreaterThan(0);
        result.Value.stream.Dispose();
    }

    [Fact]
    public async Task GetFloorplanAsync_WithNonExistentFile_ShouldReturnNull()
    {
        var result = await _service.GetFloorplanAsync("tenant_123/floorplans/nonexistent.png");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteFloorplanAsync_WithExistingFile_ShouldDeleteSuccessfully()
    {
        var file = CreateMockFormFile("test.png", "image/png", CreateTestPngImage());
        var (filePath, _, _, _) = await _service.SaveFloorplanAsync(file, Guid.NewGuid(), Guid.NewGuid());
        var fullPath = Path.Combine(_testStoragePath, filePath);
        File.Exists(fullPath).Should().BeTrue();

        await _service.DeleteFloorplanAsync(filePath);

        File.Exists(fullPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFloorplanAsync_WithNonExistentFile_ShouldNotThrow()
    {
        Func<Task> act = async () => await _service.DeleteFloorplanAsync("tenant_123/floorplans/nonexistent.png");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetImageDimensionsAsync_WithValidPng_ShouldReturnCorrectDimensions()
    {
        using var stream = new MemoryStream(CreateTestPngImage());

        var (width, height) = await _service.GetImageDimensionsAsync(stream);

        width.Should().BeGreaterThan(0);
        height.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetImageDimensionsAsync_WithValidJpeg_ShouldReturnCorrectDimensions()
    {
        using var stream = new MemoryStream(CreateTestJpegImage());

        var (width, height) = await _service.GetImageDimensionsAsync(stream);

        width.Should().BeGreaterThan(0);
        height.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetImageDimensionsAsync_WithInvalidImage_ShouldThrowException()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });

        Func<Task> act = async () => await _service.GetImageDimensionsAsync(stream);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SaveFloorplanFromStreamAsync_WithValidPng_ShouldSaveSuccessfully()
    {
        var siteId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        using var stream = new MemoryStream(CreateTestPngImage());

        var filePath = await _service.SaveFloorplanFromStreamAsync(stream, "image/png", siteId, tenantId);

        filePath.Should().NotBeNullOrEmpty();
        filePath.Should().Contain($"tenant_{tenantId}");
        filePath.Should().EndWith(".png");
        File.Exists(Path.Combine(_testStoragePath, filePath)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFloorplanFromStreamAsync_WithJpeg_ShouldSaveWithJpgExtension()
    {
        using var stream = new MemoryStream(CreateTestJpegImage());

        var filePath = await _service.SaveFloorplanFromStreamAsync(stream, "image/jpeg", Guid.NewGuid(), Guid.NewGuid());

        filePath.Should().EndWith(".jpg");
        File.Exists(Path.Combine(_testStoragePath, filePath)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFloorplanFromStreamAsync_WithEmptyStream_ShouldThrowArgumentException()
    {
        using var stream = new MemoryStream();

        Func<Task> act = async () => await _service.SaveFloorplanFromStreamAsync(
            stream, "image/png", Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*empty*");
    }

    [Fact]
    public async Task SaveFloorplanFromStreamAsync_ShouldCreateTenantDirectory()
    {
        var tenantId = Guid.NewGuid();
        using var stream = new MemoryStream(CreateTestPngImage());

        var tenantDir = Path.Combine(_testStoragePath, $"tenant_{tenantId}", "floorplans");
        Directory.Exists(tenantDir).Should().BeFalse();

        await _service.SaveFloorplanFromStreamAsync(stream, "image/png", Guid.NewGuid(), tenantId);

        Directory.Exists(tenantDir).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFloorplanAsync_ShouldCreateTenantDirectory()
    {
        var tenantId = Guid.NewGuid();
        var file = CreateMockFormFile("test.png", "image/png", CreateTestPngImage());

        var tenantDir = Path.Combine(_testStoragePath, $"tenant_{tenantId}", "floorplans");
        Directory.Exists(tenantDir).Should().BeFalse();

        await _service.SaveFloorplanAsync(file, Guid.NewGuid(), tenantId);

        Directory.Exists(tenantDir).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFloorplanAsync_WithSameSiteId_ShouldReplaceFile()
    {
        var siteId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var file1 = CreateMockFormFile("test1.png", "image/png", CreateTestPngImage());
        var file2 = CreateMockFormFile("test2.png", "image/png", CreateTestPngImage());

        var (filePath1, _, _, _) = await _service.SaveFloorplanAsync(file1, siteId, tenantId);
        var (filePath2, _, _, _) = await _service.SaveFloorplanAsync(file2, siteId, tenantId);

        filePath2.Should().NotBe(filePath1);
        File.Exists(Path.Combine(_testStoragePath, filePath2)).Should().BeTrue();
    }

    private static IFormFile CreateMockFormFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        var file = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.ContentType).Returns(contentType);
        file.Setup(f => f.Length).Returns(content.Length);
        file.Setup(f => f.OpenReadStream()).Returns(stream);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream target, CancellationToken token) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(target, token);
            });
        return file.Object;
    }

    private static byte[] CreateTestPngImage()
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(10, 10);
        image.Mutate(x => x.BackgroundColor(Color.Red));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static byte[] CreateTestJpegImage()
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(10, 10);
        image.Mutate(x => x.BackgroundColor(Color.Blue));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }
}
