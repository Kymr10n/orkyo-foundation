using System.Net;
using System.Text;
using Api.Security.Challenge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Security.Challenge;

public class CloudflareTurnstileProviderTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(string secretKey = "test-secret") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.TurnstileSecretKey] = secretKey
            })
            .Build();

    private static HttpClient FakeClient(string json) =>
        new(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

    private static CloudflareTurnstileProvider Create(string json, string secretKey = "test-secret") =>
        new(FakeClient(json), BuildConfig(secretKey), Mock.Of<ILogger<CloudflareTurnstileProvider>>());

    // ── VerifyAsync: success ──────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_TurnstileReturnsSuccess_ReturnsSuccess()
    {
        var sut = Create("""{"success":true}""");
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
    }

    // ── VerifyAsync: failure ──────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_TurnstileReturnsFailureWithErrorCodes_ReturnsFailureWithErrors()
    {
        var sut = Create("""{"success":false,"error-codes":["invalid-input-response"]}""");
        var result = await sut.VerifyAsync("bad-token", "1.2.3.4");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid-input-response");
    }

    [Fact]
    public async Task VerifyAsync_TurnstileReturnsFailureWithMultipleErrors_JoinsErrorCodes()
    {
        var sut = Create("""{"success":false,"error-codes":["missing-input-response","bad-request"]}""");
        var result = await sut.VerifyAsync("bad-token", "1.2.3.4");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("missing-input-response, bad-request");
    }

    [Fact]
    public async Task VerifyAsync_TurnstileReturnsFailureWithNoErrorCodes_ReturnsEmptyErrorCode()
    {
        var sut = Create("""{"success":false}""");
        var result = await sut.VerifyAsync("bad-token", "1.2.3.4");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().BeEmpty();
    }

    // ── VerifyAsync: HTTP/network failure (fail-open) ─────────────────────────

    [Fact]
    public async Task VerifyAsync_HttpClientThrows_FailsOpenWithMarkerCode()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));
        var sut = new CloudflareTurnstileProvider(
            new HttpClient(handler),
            BuildConfig(),
            Mock.Of<ILogger<CloudflareTurnstileProvider>>());

        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().Be("verification_unavailable");
    }

    [Fact]
    public async Task VerifyAsync_MalformedJson_FailsOpenWithMarkerCode()
    {
        var sut = Create("not-json");
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().Be("verification_unavailable");
    }
}
