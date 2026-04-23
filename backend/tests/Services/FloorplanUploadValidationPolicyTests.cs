using Api.Services;
using FluentAssertions;

namespace Orkyo.Foundation.Tests.Services;

public class FloorplanUploadValidationPolicyTests
{
    [Fact]
    public void BytesPerMegabyte_IsLockedTo_1MiB()
    {
        FloorplanUploadValidationPolicy.BytesPerMegabyte.Should().Be(1024L * 1024L);
    }

    // --- AssertNonEmpty ---

    [Fact]
    public void AssertNonEmpty_Throws_OnZeroLength()
    {
        var act = () => FloorplanUploadValidationPolicy.AssertNonEmpty(0);
        act.Should().Throw<ArgumentException>().WithMessage("File is empty");
    }

    [Fact]
    public void AssertNonEmpty_Passes_OnPositiveLength()
    {
        var act = () => FloorplanUploadValidationPolicy.AssertNonEmpty(1);
        act.Should().NotThrow();
    }

    // --- AssertWithinSizeLimit ---

    [Fact]
    public void AssertWithinSizeLimit_Passes_WhenAtLimit()
    {
        var act = () => FloorplanUploadValidationPolicy.AssertWithinSizeLimit(5 * 1024L * 1024L, 5);
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertWithinSizeLimit_Throws_WhenOverLimit()
    {
        var act = () => FloorplanUploadValidationPolicy.AssertWithinSizeLimit(5 * 1024L * 1024L + 1, 5);
        act.Should().Throw<ArgumentException>()
           .WithMessage("File size exceeds maximum of 5MB");
    }

    [Fact]
    public void AssertWithinSizeLimit_MessageIncludesConfiguredLimitInMb()
    {
        var act = () => FloorplanUploadValidationPolicy.AssertWithinSizeLimit(20L * 1024 * 1024, 10);
        act.Should().Throw<ArgumentException>()
           .Which.Message.Should().Contain("10MB");
    }

    // --- ParseAllowedMimeTypes ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseAllowedMimeTypes_ReturnsEmpty_OnNullOrBlank(string? input)
    {
        FloorplanUploadValidationPolicy.ParseAllowedMimeTypes(input!).Should().BeEmpty();
    }

    [Fact]
    public void ParseAllowedMimeTypes_TrimsAndDropsEmptyEntries()
    {
        var result = FloorplanUploadValidationPolicy.ParseAllowedMimeTypes(" image/png , image/jpeg ,, ");

        result.Should().BeEquivalentTo(new[] { "image/png", "image/jpeg" }, opt => opt.WithStrictOrdering());
    }

    [Fact]
    public void ParseAllowedMimeTypes_PreservesOrder()
    {
        var result = FloorplanUploadValidationPolicy.ParseAllowedMimeTypes("image/jpeg,image/png");
        result.Should().Equal("image/jpeg", "image/png");
    }

    // --- AssertMimeAllowed ---

    [Fact]
    public void AssertMimeAllowed_Passes_WhenInAllowList()
    {
        var act = () => FloorplanUploadValidationPolicy.AssertMimeAllowed(
            "image/png", new[] { "image/png", "image/jpeg" });
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertMimeAllowed_Throws_WhenNotInAllowList()
    {
        var act = () => FloorplanUploadValidationPolicy.AssertMimeAllowed(
            "image/gif", new[] { "image/png", "image/jpeg" });
        act.Should().Throw<ArgumentException>()
           .Which.Message.Should().Contain("image/gif").And.Contain("image/png").And.Contain("image/jpeg");
    }

    [Fact]
    public void AssertMimeAllowed_IsCaseSensitive_ToMatchExistingWireContract()
    {
        // Caller is expected to lowercase the detected MIME (matching ImageSharp's
        // DefaultMimeType lowercase output and the existing SaaS behavior).
        var act = () => FloorplanUploadValidationPolicy.AssertMimeAllowed(
            "IMAGE/PNG", new[] { "image/png" });
        act.Should().Throw<ArgumentException>();
    }
}
