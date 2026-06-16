using Api.Services;
using Api.Tests.TestHelpers;
using Xunit;

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
    public void DetectMimeType_GifMagicBytes_ReturnsNull()
    {
        var gif = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00 }; // "GIF89a..."

        ImageHeaderReader.DetectMimeType(gif).Should().BeNull();
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
