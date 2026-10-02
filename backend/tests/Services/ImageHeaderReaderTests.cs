using Api.Services;
using Api.Tests.TestHelpers;

namespace Orkyo.Foundation.Tests.Services;

public class ImageHeaderReaderTests
{
    [Fact]
    public void DetectMimeType_Png_ReturnsImagePng()
    {
        var png = TestImageFactory.Png(4, 4);

        ImageHeaderReader.DetectMimeType(png).Should().Be("image/png");
    }

    [Fact]
    public void DetectMimeType_Jpeg_ReturnsImageJpeg()
    {
        var jpeg = TestImageFactory.Jpeg(4, 4);

        ImageHeaderReader.DetectMimeType(jpeg).Should().Be("image/jpeg");
    }

    [Theory]
    [InlineData("not an image at all")]
    [InlineData("")]
    public void DetectMimeType_UnrecognisedData_ReturnsNull(string text)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);

        ImageHeaderReader.DetectMimeType(bytes).Should().BeNull();
    }

    [Fact]
    public void DetectMimeType_Webp_ReturnsImageWebp()
    {
        ImageHeaderReader.DetectMimeType(TestImageFactory.Webp(4, 4)).Should().Be("image/webp");
    }

    [Fact]
    public void DetectMimeType_Gif_ReturnsImageGif()
    {
        ImageHeaderReader.DetectMimeType(TestImageFactory.Gif(4, 4)).Should().Be("image/gif");
    }

    [Fact]
    public void DetectMimeType_Bmp_ReturnsImageBmp()
    {
        ImageHeaderReader.DetectMimeType(TestImageFactory.Bmp(4, 4)).Should().Be("image/bmp");
    }

    [Fact]
    public void DetectMimeType_RiffWithoutWebpFormType_ReturnsNull()
    {
        var wav = "RIFF\0\0\0\0WAVEfmt "u8.ToArray();

        ImageHeaderReader.DetectMimeType(wav).Should().BeNull();
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(1, 1)]
    [InlineData(1920, 1080)]
    public void TryGetDimensions_Webp_ReturnsCanvasDimensions(int width, int height)
    {
        var ok = ImageHeaderReader.TryGetDimensions(TestImageFactory.Webp(width, height), out var w, out var h);

        ok.Should().BeTrue();
        w.Should().Be(width);
        h.Should().Be(height);
    }

    [Fact]
    public void TryGetDimensions_LossyWebp_ReadsFrameHeaderAndIgnoresScalingBits()
    {
        var webp = new byte[]
        {
            0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, // "RIFF", size (unused)
            0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20, // "WEBP", "VP8 "
            0x00, 0x00, 0x00, 0x00, 0x10, 0x02, 0x00, // chunk size (unused), frame tag
            0x9D, 0x01, 0x2A, // start code
            0x40, 0x41, // width 320 with scaling bits set
            0xF0, 0x80, // height 240 with scaling bits set
        };

        ImageHeaderReader.TryGetDimensions(webp, out var w, out var h).Should().BeTrue();
        w.Should().Be(320);
        h.Should().Be(240);
    }

    [Fact]
    public void TryGetDimensions_LosslessWebp_ReadsPackedDimensions()
    {
        var webp = new byte[30];
        "RIFF"u8.CopyTo(webp);
        "WEBPVP8L"u8.CopyTo(webp.AsSpan(8));
        webp[20] = 0x2F; // signature
        // 14-bit width-1 = 319 (0x13F), 14-bit height-1 = 239 (0xEF): 0x13F | 0xEF << 14 = 0x3BC13F.
        webp[21] = 0x3F;
        webp[22] = 0xC1;
        webp[23] = 0x3B;
        webp[24] = 0x00;

        ImageHeaderReader.TryGetDimensions(webp, out var w, out var h).Should().BeTrue();
        w.Should().Be(320);
        h.Should().Be(240);
    }

    [Fact]
    public void TryGetDimensions_WebpWithUnknownFirstChunk_ReturnsFalse()
    {
        var webp = TestImageFactory.Webp(10, 10);
        "ALPH"u8.CopyTo(webp.AsSpan(12));

        ImageHeaderReader.TryGetDimensions(webp, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(1, 1)]
    [InlineData(1920, 1080)]
    public void TryGetDimensions_Gif_ReturnsScreenDimensions(int width, int height)
    {
        var ok = ImageHeaderReader.TryGetDimensions(TestImageFactory.Gif(width, height), out var w, out var h);

        ok.Should().BeTrue();
        w.Should().Be(width);
        h.Should().Be(height);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(1, 1)]
    [InlineData(1920, 1080)]
    public void TryGetDimensions_Bmp_ReturnsHeaderDimensions(int width, int height)
    {
        var ok = ImageHeaderReader.TryGetDimensions(TestImageFactory.Bmp(width, height), out var w, out var h);

        ok.Should().BeTrue();
        w.Should().Be(width);
        h.Should().Be(height);
    }

    [Fact]
    public void TryGetDimensions_TopDownBmp_ReadsNegativeHeightAsPositive()
    {
        var bmp = TestImageFactory.Bmp(10, 10);
        BitConverter.GetBytes(-10).CopyTo(bmp, 22);

        ImageHeaderReader.TryGetDimensions(bmp, out var w, out var h).Should().BeTrue();
        w.Should().Be(10);
        h.Should().Be(10);
    }

    [Fact]
    public void TryGetDimensions_CoreHeaderBmp_ReadsSixteenBitDimensions()
    {
        var bmp = new byte[26];
        "BM"u8.CopyTo(bmp);
        bmp[14] = 12; // BITMAPCOREHEADER size
        bmp[18] = 0x40; // width 320
        bmp[19] = 0x01;
        bmp[20] = 0xF0; // height 240
        bmp[21] = 0x00;

        ImageHeaderReader.TryGetDimensions(bmp, out var w, out var h).Should().BeTrue();
        w.Should().Be(320);
        h.Should().Be(240);
    }

    [Fact]
    public void TryGetDimensions_TruncatedHeaders_ReturnFalse()
    {
        ImageHeaderReader.TryGetDimensions(TestImageFactory.Webp(10, 10).AsSpan(0, 20), out _, out _).Should().BeFalse();
        ImageHeaderReader.TryGetDimensions(TestImageFactory.Gif(10, 10).AsSpan(0, 8), out _, out _).Should().BeFalse();
        ImageHeaderReader.TryGetDimensions(TestImageFactory.Bmp(10, 10).AsSpan(0, 20), out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(1, 1)]
    [InlineData(1920, 1080)]
    public void TryGetDimensions_Png_ReturnsHeaderDimensions(int width, int height)
    {
        var png = TestImageFactory.Png(width, height);

        var ok = ImageHeaderReader.TryGetDimensions(png, out var w, out var h);

        ok.Should().BeTrue();
        w.Should().Be(width);
        h.Should().Be(height);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(1, 1)]
    [InlineData(1920, 1080)]
    public void TryGetDimensions_Jpeg_ReturnsHeaderDimensions(int width, int height)
    {
        var jpeg = TestImageFactory.Jpeg(width, height);

        var ok = ImageHeaderReader.TryGetDimensions(jpeg, out var w, out var h);

        ok.Should().BeTrue();
        w.Should().Be(width);
        h.Should().Be(height);
    }

    [Fact]
    public void TryGetDimensions_UnrecognisedData_ReturnsFalse()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("nonsense");

        var ok = ImageHeaderReader.TryGetDimensions(bytes, out var w, out var h);

        ok.Should().BeFalse();
        w.Should().Be(0);
        h.Should().Be(0);
    }

    [Fact]
    public void TryGetDimensions_TruncatedPngHeader_ReturnsFalse()
    {
        var png = TestImageFactory.Png(10, 10).AsSpan(0, 12).ToArray(); // signature + 4 bytes only

        ImageHeaderReader.TryGetDimensions(png, out _, out _).Should().BeFalse();
    }
}
