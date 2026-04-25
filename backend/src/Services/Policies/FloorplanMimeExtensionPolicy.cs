namespace Api.Services;

/// <summary>
/// Bidirectional MIME-type ↔ filename-extension policy for floorplan image
/// uploads. Single canonical source of (1) the allow-list of MIME types
/// considered "supported" for floorplan storage and (2) the reverse mapping
/// used to derive a response Content-Type from a stored file extension.
/// Structurally identical for any floorplan storage backend in either
/// multi-tenant SaaS or single-tenant Community deployments.
/// </summary>
public static class FloorplanMimeExtensionPolicy
{
    public const string PngMimeType = "image/png";
    public const string JpegMimeType = "image/jpeg";
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
            default: extension = string.Empty; return false;
        }
    }

    /// <summary>
    /// Map a stored file extension back to a Content-Type for download
    /// responses. Unknown extensions fall back to
    /// <see cref="OctetStreamMimeType"/> to avoid silent mistyping.
    /// Accepts both <c>.jpg</c> and <c>.jpeg</c>; case-insensitive on input.
    /// </summary>
    public static string GetMimeForExtension(string extension)
    {
        return (extension ?? string.Empty).ToLowerInvariant() switch
        {
            ".png" => PngMimeType,
            ".jpg" or ".jpeg" => JpegMimeType,
            _ => OctetStreamMimeType,
        };
    }

    /// <summary>
    /// Pick a storage extension for a write path that may receive any
    /// caller-supplied MIME type. Defaults to <c>.png</c> for unknown inputs
    /// to preserve historical SaaS behavior on the embedded-resource demo
    /// floorplan write path. Prefer <see cref="TryGetExtensionForMime"/> at
    /// validating boundaries.
    /// </summary>
    public static string GetExtensionForMimeOrPngFallback(string mimeType) =>
        TryGetExtensionForMime(mimeType, out var ext) ? ext : ".png";
}
