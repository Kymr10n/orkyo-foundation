using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class FloorplanMimeExtensionPolicyTests
{
    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("IMAGE/PNG", ".png")]
    [InlineData("Image/Jpeg", ".jpg")]
    public void TryGetExtensionForMime_MapsSupportedMimesCaseInsensitively(string mime, string expected)
    {
        FloorplanMimeExtensionPolicy.TryGetExtensionForMime(mime, out var ext).Should().BeTrue();
        ext.Should().Be(expected);
    }

    [Theory]
    [InlineData("image/webp")]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("")]
    public void TryGetExtensionForMime_ReturnsFalseForUnsupportedMimes(string mime)
    {
        FloorplanMimeExtensionPolicy.TryGetExtensionForMime(mime, out var ext).Should().BeFalse();
        ext.Should().BeEmpty();
    }

    [Fact]
    public void TryGetExtensionForMime_TreatsNullAsUnsupported()
    {
        FloorplanMimeExtensionPolicy.TryGetExtensionForMime(null!, out var ext).Should().BeFalse();
        ext.Should().BeEmpty();
    }

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".PNG", "image/png")]
    [InlineData(".JPEG", "image/jpeg")]
    public void GetMimeForExtension_MapsKnownExtensionsCaseInsensitively(string ext, string expected) =>
        FloorplanMimeExtensionPolicy.GetMimeForExtension(ext).Should().Be(expected);

    [Theory]
    [InlineData(".webp")]
    [InlineData(".gif")]
    [InlineData("")]
    [InlineData(".unknown")]
    public void GetMimeForExtension_FallsBackToOctetStreamForUnknown(string ext) =>
        FloorplanMimeExtensionPolicy.GetMimeForExtension(ext).Should().Be("application/octet-stream");

    [Fact]
    public void GetMimeForExtension_TreatsNullAsOctetStream() =>
        FloorplanMimeExtensionPolicy.GetMimeForExtension(null!).Should().Be("application/octet-stream");

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/webp", ".png")]    // historical demo-floorplan fallback
    [InlineData("anything", ".png")]
    public void GetExtensionForMimeOrPngFallback_FallsBackToPngForUnsupported(string mime, string expected) =>
        FloorplanMimeExtensionPolicy.GetExtensionForMimeOrPngFallback(mime).Should().Be(expected);

    [Fact]
    public void Constants_AreStable()
    {
        FloorplanMimeExtensionPolicy.PngMimeType.Should().Be("image/png");
        FloorplanMimeExtensionPolicy.JpegMimeType.Should().Be("image/jpeg");
        FloorplanMimeExtensionPolicy.OctetStreamMimeType.Should().Be("application/octet-stream");
    }
}
