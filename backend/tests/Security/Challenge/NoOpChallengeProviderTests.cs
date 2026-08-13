using Api.Security.Challenge;

namespace Orkyo.Foundation.Tests.Security.Challenge;

public class NoOpChallengeProviderTests
{
    private readonly NoOpChallengeProvider _sut = new();

    [Fact]
    public async Task VerifyAsync_AlwaysReturnsSuccess()
    {
        var result = await _sut.VerifyAsync("any-token", "1.2.3.4");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_EmptyToken_StillReturnsSuccess()
    {
        var result = await _sut.VerifyAsync("", "");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_ErrorCode_IsNull()
    {
        var result = await _sut.VerifyAsync("any-token", "1.2.3.4");
        result.ErrorCode.Should().BeNull();
    }
}
