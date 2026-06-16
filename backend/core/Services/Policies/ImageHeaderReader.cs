namespace Api.Services;

/// <summary>
/// Minimal, dependency-free reader for the only two image formats Orkyo accepts
/// for floorplan uploads (PNG and JPEG). It inspects file headers to (1) identify
/// the format by its magic bytes and (2) extract pixel dimensions — the only image
/// operations the platform performs. No pixel decoding, resizing, or re-encoding is
/// done anywhere, so a full imaging library is unnecessary.
///
/// Structurally identical for any floorplan storage backend in either multi-tenant
/// SaaS or single-tenant Community deployments.
/// </summary>
public static class ImageHeaderReader
{
    public const string PngMimeType = "image/png";
    public const string JpegMimeType = "image/jpeg";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Identifies a supported image by its magic bytes. Returns the canonical
    /// lowercase MIME type (<see cref="PngMimeType"/> or <see cref="JpegMimeType"/>),
    /// or <c>null</c> if the data is not a recognised/supported image.
    /// </summary>
    public static string? DetectMimeType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= PngSignature.Length && data[..PngSignature.Length].SequenceEqual(PngSignature))
            return PngMimeType;

        // JPEG: SOI (FF D8) immediately followed by a marker (FF).
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return JpegMimeType;

        return null;
    }

    /// <summary>
    /// Reads the pixel dimensions of a PNG or JPEG. Returns <c>false</c> if the data
    /// is not a supported image or its header cannot be parsed.
    /// </summary>
    public static bool TryGetDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        return DetectMimeType(data) switch
        {
            PngMimeType => TryReadPngDimensions(data, out width, out height),
            JpegMimeType => TryReadJpegDimensions(data, out width, out height),
            _ => false,
        };
    }

    // PNG: the IHDR chunk always comes first, immediately after the 8-byte signature.
    // Its data starts at offset 16 with width (4 bytes, big-endian) then height.
    private static bool TryReadPngDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 24)
            return false;

        width = ReadBigEndianInt32(data, 16);
        height = ReadBigEndianInt32(data, 20);
        return width > 0 && height > 0;
    }

    // JPEG: walk the marker segments after SOI until a Start-Of-Frame (SOFn) marker,
    // whose payload carries the image height and width.
    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        var pos = 2; // skip SOI (FF D8)
        while (pos + 1 < data.Length)
        {
            if (data[pos] != 0xFF)
                return false; // not aligned on a marker — malformed

            var marker = data[pos + 1];

            // Fill byte: any number of 0xFF may pad before the real marker.
            if (marker == 0xFF)
            {
                pos++;
                continue;
            }

            pos += 2;

            // Standalone markers (SOI/EOI/RSTn/TEM) carry no length-prefixed segment.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
                continue;

            if (pos + 1 >= data.Length)
                return false;

            var segmentLength = (data[pos] << 8) | data[pos + 1];
            if (segmentLength < 2)
                return false;

            // SOFn markers carry dimensions: C0–CF, excluding DHT (C4), JPG (C8), DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                // Payload after the 2 length bytes: precision (1), height (2), width (2).
                if (pos + 7 > data.Length)
                    return false;
                height = (data[pos + 3] << 8) | data[pos + 4];
                width = (data[pos + 5] << 8) | data[pos + 6];
                return width > 0 && height > 0;
            }

            pos += segmentLength;
        }

        return false;
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
