namespace Api.Services;

/// <summary>
/// Minimal, dependency-free reader for the image formats Orkyo accepts for floorplan
/// uploads (PNG, JPEG, WebP, GIF and BMP). It inspects file headers to (1) identify the
/// format by its magic bytes and (2) extract pixel dimensions — the only image operations
/// the platform performs. No pixel decoding, resizing, or re-encoding is done anywhere,
/// so a full imaging library is unnecessary. The set is limited to formats every supported
/// browser renders natively, since the floorplan is drawn as an SVG &lt;image&gt; as uploaded.
///
/// Structurally identical for any floorplan storage backend in either multi-tenant
/// SaaS or single-tenant Community deployments.
/// </summary>
public static class ImageHeaderReader
{
    public const string PngMimeType = "image/png";
    public const string JpegMimeType = "image/jpeg";
    public const string WebpMimeType = "image/webp";
    public const string GifMimeType = "image/gif";
    public const string BmpMimeType = "image/bmp";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> RiffSignature => "RIFF"u8;
    private static ReadOnlySpan<byte> WebpSignature => "WEBP"u8;
    private static ReadOnlySpan<byte> Gif87Signature => "GIF87a"u8;
    private static ReadOnlySpan<byte> Gif89Signature => "GIF89a"u8;
    private static ReadOnlySpan<byte> BmpSignature => "BM"u8;

    /// <summary>
    /// Identifies a supported image by its magic bytes. Returns the canonical
    /// lowercase MIME type, or <c>null</c> if the data is not a recognised/supported image.
    /// </summary>
    public static string? DetectMimeType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature))
            return PngMimeType;

        // JPEG: SOI (FF D8) immediately followed by a marker (FF).
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return JpegMimeType;

        // WebP: a RIFF container whose form type (offset 8) is "WEBP".
        if (data.Length >= 12 && data.StartsWith(RiffSignature) && data.Slice(8, 4).SequenceEqual(WebpSignature))
            return WebpMimeType;

        if (data.StartsWith(Gif87Signature) || data.StartsWith(Gif89Signature))
            return GifMimeType;

        if (data.StartsWith(BmpSignature))
            return BmpMimeType;

        return null;
    }

    /// <summary>
    /// Reads the pixel dimensions of a supported image. Returns <c>false</c> if the data
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
            WebpMimeType => TryReadWebpDimensions(data, out width, out height),
            GifMimeType => TryReadGifDimensions(data, out width, out height),
            BmpMimeType => TryReadBmpDimensions(data, out width, out height),
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

    // WebP: the first chunk after the 12-byte RIFF header decides the layout. Its FourCC
    // sits at offset 12 and its payload starts at offset 20.
    private static bool TryReadWebpDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 30)
            return false;

        var chunk = data.Slice(12, 4);
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Lossy: 3-byte frame tag, then the start code 9D 01 2A, then 14-bit width and
            // height (the top two bits of each are scaling hints).
            if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A)
                return false;
            width = ReadLittleEndianUInt16(data, 26) & 0x3FFF;
            height = ReadLittleEndianUInt16(data, 28) & 0x3FFF;
        }
        else if (chunk.SequenceEqual("VP8L"u8))
        {
            // Lossless: signature byte 2F, then 14-bit width-1 and height-1 packed little-endian.
            if (data[20] != 0x2F)
                return false;
            var bits = ReadLittleEndianInt32(data, 21);
            width = (bits & 0x3FFF) + 1;
            height = ((bits >> 14) & 0x3FFF) + 1;
        }
        else if (chunk.SequenceEqual("VP8X"u8))
        {
            // Extended: flags (4 bytes), then 24-bit canvas width-1 and height-1.
            width = ReadLittleEndianUInt24(data, 24) + 1;
            height = ReadLittleEndianUInt24(data, 27) + 1;
        }
        else
        {
            return false;
        }

        return width > 0 && height > 0;
    }

    // GIF: the logical screen descriptor follows the 6-byte signature — width then height,
    // each a little-endian 16-bit value.
    private static bool TryReadGifDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 10)
            return false;

        width = ReadLittleEndianUInt16(data, 6);
        height = ReadLittleEndianUInt16(data, 8);
        return width > 0 && height > 0;
    }

    // BMP: the DIB header starts at offset 14 with its own size. The 12-byte BITMAPCOREHEADER
    // stores 16-bit dimensions; every later header stores 32-bit ones, where a negative height
    // only means the rows are stored top-down.
    private static bool TryReadBmpDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 26)
            return false;

        var headerSize = ReadLittleEndianInt32(data, 14);
        if (headerSize == 12)
        {
            width = ReadLittleEndianUInt16(data, 18);
            height = ReadLittleEndianUInt16(data, 20);
        }
        else
        {
            width = ReadLittleEndianInt32(data, 18);
            height = Math.Abs(ReadLittleEndianInt32(data, 22));
        }

        return width > 0 && height > 0;
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static int ReadLittleEndianUInt16(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8);

    private static int ReadLittleEndianUInt24(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);

    private static int ReadLittleEndianInt32(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
