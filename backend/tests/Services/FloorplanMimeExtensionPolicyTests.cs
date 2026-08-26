using Api.Services;
using AwesomeAssertions;
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

    [Fact]
    public void Constants_AreStable()
    {
        FloorplanMimeExtensionPolicy.PngMimeType.Should().Be("image/png");
        FloorplanMimeExtensionPolicy.JpegMimeType.Should().Be("image/jpeg");
        FloorplanMimeExtensionPolicy.OctetStreamMimeType.Should().Be("application/octet-stream");
    }
}
