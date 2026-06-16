using System.IO.Compression;
using System.Text;

namespace Api.Tests.TestHelpers;

/// <summary>
/// Builds minimal, valid PNG and JPEG byte payloads for upload/validation tests,
/// replacing the previous dependency on a full imaging library. Orkyo only reads
/// image headers (format + dimensions), so these fixtures need correct headers,
/// not richly-decodable pixel content.
/// </summary>
public static class TestImageFactory
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Builds a valid RGBA PNG of the given dimensions.</summary>
    public static byte[] Png(int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write(PngSignature);

        // IHDR: width, height, bit depth 8, colour type 6 (RGBA), no compression/filter/interlace.
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // colour type RGBA
        WriteChunk(ms, "IHDR", ihdr);

        // IDAT: raw scanlines (filter byte 0 + RGBA pixels) zlib-compressed.
        var raw = new byte[height * (1 + width * 4)];
        WriteChunk(ms, "IDAT", ZlibCompress(raw));

        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a minimal JPEG with a valid SOI/APP0/SOF0/EOI marker structure carrying
    /// the given dimensions — sufficient for header-based format and dimension detection.
    /// </summary>
    public static byte[] Jpeg(int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]); // SOI
        // APP0 / JFIF
        ms.Write([0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        // SOF0: length 17, precision 8, height, width, 3 components.
        ms.Write(
        [
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
            0x03,
            0x01, 0x22, 0x00,
            0x02, 0x11, 0x01,
            0x03, 0x11, 0x01,
        ]);
        ms.Write([0xFF, 0xD9]); // EOI
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        var len = new byte[4];
        WriteBigEndian(len, 0, data.Length);
        s.Write(len);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32(typeBytes, data));
        s.Write(crc);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
            crc = Step(crc, b);
        foreach (var b in data)
            crc = Step(crc, b);
        return crc ^ 0xFFFFFFFFu;

        static uint Step(uint crc, byte b)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            return crc;
        }
    }
}
