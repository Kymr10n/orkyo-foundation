using Api.Configuration;
using Orkyo.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace Api.Services;

public interface IFileStorageService
{
    /// <summary>
    /// Validates, detects format via magic bytes, and saves a floorplan image.
    /// Returns (relativePath, detectedMimeType, widthPx, heightPx).
    /// </summary>
    Task<(string relativePath, string detectedMimeType, int widthPx, int heightPx)> SaveFloorplanAsync(IFormFile file, Guid siteId, Guid tenantId);
    Task<string> SaveFloorplanFromStreamAsync(Stream stream, string mimeType, Guid siteId, Guid tenantId);
    Task<(Stream stream, string mimeType)?> GetFloorplanAsync(string filePath);
    Task DeleteFloorplanAsync(string filePath);
    Task<(int width, int height)> GetImageDimensionsAsync(Stream stream);
}

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorageService> _logger;
    private readonly ITenantSettingsService _settingsService;

    public LocalFileStorageService(
        IConfiguration configuration,
        ILogger<LocalFileStorageService> logger,
        ITenantSettingsService settingsService)
    {
        _basePath = Path.GetFullPath(configuration.GetRequired(ConfigKeys.FileStoragePath));
        _logger = logger;
        _settingsService = settingsService;

        // Ensure base directory exists
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
            _logger.LogInformation("Created storage directory: {Path}", _basePath);
        }
    }

    /// <summary>Validates that <paramref name="fullPath"/> is inside <see cref="_basePath"/>.</summary>
    private void AssertWithinBasePath(string fullPath)
    {
        try
        {
            LocalFileStorageGuard.AssertWithinBasePath(_basePath, fullPath);
        }
        catch (ArgumentException)
        {
            _logger.LogError("Path traversal attempt detected: {Path}", fullPath);
            throw;
        }
    }

    public async Task<(string relativePath, string detectedMimeType, int widthPx, int heightPx)> SaveFloorplanAsync(IFormFile file, Guid siteId, Guid tenantId)
    {
        FloorplanUploadValidationPolicy.AssertNonEmpty(file.Length);

        var settings = await _settingsService.GetSettingsAsync();
        FloorplanUploadValidationPolicy.AssertWithinSizeLimit(file.Length, settings.Upload_MaxFileSizeMb);

        // Stream the upload directly — no MemoryStream heap allocation.
        // DetectFormatAsync reads only the magic-byte header; IdentifyAsync reads
        // just enough to extract dimensions without decoding the full image.
        await using var sourceStream = file.OpenReadStream();

        IImageFormat imageFormat;
        try
        {
            var detected = await Image.DetectFormatAsync(sourceStream);
            if (detected == null)
                throw new ArgumentException("File is not a recognised image format");
            imageFormat = detected;
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException("File is not a valid image", ex);
        }

        var detectedMimeType = imageFormat.DefaultMimeType.ToLowerInvariant();

        // Validate MIME type before reading dimensions (fail fast)
        var allowedMimeTypes = FloorplanUploadValidationPolicy.ParseAllowedMimeTypes(settings.Upload_AllowedMimeTypes);
        FloorplanUploadValidationPolicy.AssertMimeAllowed(detectedMimeType, allowedMimeTypes);

        if (!FloorplanMimeExtensionPolicy.TryGetExtensionForMime(detectedMimeType, out var extension))
            throw new ArgumentException($"Unsupported image format: {detectedMimeType}");

        // Rewind and identify dimensions without decoding the full image
        int widthPx, heightPx;
        try
        {
            sourceStream.Position = 0;
            var info = await Image.IdentifyAsync(sourceStream);
            if (info == null)
                throw new ArgumentException("Could not read image dimensions");
            widthPx = info.Width;
            heightPx = info.Height;
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException("File is not a valid image", ex);
        }

        var fileName = FloorplanStoragePathPolicy.BuildFileName(siteId, Guid.NewGuid(), extension);
        var tenantDirectory = FloorplanStoragePathPolicy.BuildTenantFloorplanDirectory(_basePath, tenantId);
        var fullPath = Path.Combine(tenantDirectory, fileName);
        AssertWithinBasePath(fullPath);

        var relativePath = FloorplanStoragePathPolicy.BuildRelativePath(tenantId, fileName);

        if (!Directory.Exists(tenantDirectory))
            Directory.CreateDirectory(tenantDirectory);

        // Stream directly to disk with async I/O — no intermediate buffer
        sourceStream.Position = 0;
        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await sourceStream.CopyToAsync(fileStream);

        _logger.LogInformation("Saved floorplan: {Path}, Size: {Size} bytes, Format: {Format}, Dimensions: {W}x{H}",
            relativePath, file.Length, detectedMimeType, widthPx, heightPx);

        return (relativePath, detectedMimeType, widthPx, heightPx);
    }


    public async Task<string> SaveFloorplanFromStreamAsync(Stream source, string mimeType, Guid siteId, Guid tenantId)
    {
        if (source.Length == 0)
            throw new ArgumentException("Stream is empty");

        var extension = FloorplanMimeExtensionPolicy.GetExtensionForMimeOrPngFallback(mimeType);

        var fileName = FloorplanStoragePathPolicy.BuildFileName(siteId, Guid.NewGuid(), extension);
        var tenantDirectory = FloorplanStoragePathPolicy.BuildTenantFloorplanDirectory(_basePath, tenantId);
        var fullPath = Path.Combine(tenantDirectory, fileName);
        var relativePath = FloorplanStoragePathPolicy.BuildRelativePath(tenantId, fileName);

        if (!Directory.Exists(tenantDirectory))
            Directory.CreateDirectory(tenantDirectory);

        await using var fileStream = new FileStream(fullPath, FileMode.Create);
        await source.CopyToAsync(fileStream);

        _logger.LogInformation("Saved floorplan from stream: {Path}, Size: {Size} bytes", relativePath, source.Length);

        return relativePath;
    }

    public async Task<(Stream stream, string mimeType)?> GetFloorplanAsync(string filePath)
    {
        var fullPath = Path.Combine(_basePath, filePath);
        AssertWithinBasePath(fullPath);

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Floorplan not found: {Path}", filePath);
            return null;
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var mimeType = FloorplanMimeExtensionPolicy.GetMimeForExtension(extension);

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream, mimeType);
    }

    public async Task DeleteFloorplanAsync(string filePath)
    {
        var fullPath = Path.Combine(_basePath, filePath);
        AssertWithinBasePath(fullPath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Deleted floorplan: {Path}", filePath);
        }
        else
        {
            _logger.LogWarning("Floorplan not found for deletion: {Path}", filePath);
        }

        await Task.CompletedTask;
    }

    public async Task<(int width, int height)> GetImageDimensionsAsync(Stream stream)
    {
        try
        {
            using var image = await Image.LoadAsync(stream);
            return (image.Width, image.Height);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load image dimensions");
            throw new ArgumentException("Invalid image file", ex);
        }
    }
}
