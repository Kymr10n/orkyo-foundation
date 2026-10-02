namespace Api.Services;

/// <summary>
/// MIME-type ↔ filename-extension policy for floorplan image uploads.
/// Single canonical source of the allow-list of MIME types considered
/// "supported" for floorplan storage.
/// Structurally identical for any floorplan storage backend in either
/// multi-tenant SaaS or single-tenant Community deployments.
/// </summary>
public static class FloorplanMimeExtensionPolicy
{
    // The format list has one home, ImageHeaderReader; these aliases keep the public surface.
    public const string PngMimeType = ImageHeaderReader.PngMimeType;
    public const string JpegMimeType = ImageHeaderReader.JpegMimeType;
    public const string OctetStreamMimeType = "application/octet-stream";

    /// <summary>
    /// Try to map a detected MIME type to its canonical filename extension
    /// (lowercased, leading dot included). Case-insensitive on input.
    /// Returns <c>false</c> for unsupported MIME types.
    /// </summary>
    public static bool TryGetExtensionForMime(string mimeType, out string extension)
    {
        switch ((mimeType ?? string.Empty).ToLowerInvariant())
        {
            case PngMimeType: extension = ".png"; return true;
            case JpegMimeType: extension = ".jpg"; return true;
            case ImageHeaderReader.WebpMimeType: extension = ".webp"; return true;
            case ImageHeaderReader.GifMimeType: extension = ".gif"; return true;
            case ImageHeaderReader.BmpMimeType: extension = ".bmp"; return true;
            default: extension = string.Empty; return false;
        }
    }
}
