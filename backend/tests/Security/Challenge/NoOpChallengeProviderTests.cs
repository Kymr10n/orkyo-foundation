using Api.Security.Challenge;

namespace Orkyo.Foundation.Tests.Security.Challenge;

public class NoOpChallengeProviderTests
{
    private readonly NoOpChallengeProvider _sut = new();

    [Theory]
    [InlineData("any-token", "1.2.3.4")]
    [InlineData("", "")]
    public async Task VerifyAsync_AlwaysSucceedsWithoutAnErrorCode(string token, string remoteIp)
    {
        var result = await _sut.VerifyAsync(token, remoteIp);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
    }
}
