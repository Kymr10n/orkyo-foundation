using System.Net;
using System.Text;
using Api.Security.Challenge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Security.Challenge;

public class CloudflareTurnstileProviderTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private const string AppHost = "orkyo.example";

    private static IConfiguration BuildConfig(string secretKey = "test-secret") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.TurnstileSecretKey] = secretKey,
                [ConfigKeys.AppBaseUrl] = $"https://{AppHost}/",
            })
            .Build();

    private static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    private static HttpClient FakeClient(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubHttpMessageHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

    private static CloudflareTurnstileProvider Create(
        HttpClient client,
        string environment = EnvironmentNames.Production,
        ILogger<CloudflareTurnstileProvider>? logger = null) =>
        new(client, BuildConfig(), Env(environment), logger ?? Mock.Of<ILogger<CloudflareTurnstileProvider>>());

    private static CloudflareTurnstileProvider Create(string json, string environment = EnvironmentNames.Production) =>
        Create(FakeClient(json), environment);

    private static HttpClient ThrowingClient() =>
        new(new StubHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure")));

    // ── VerifyAsync: success ──────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_TurnstileReturnsSuccessForTheAppHost_ReturnsSuccess()
    {
        var sut = Create($$"""{"success":true,"hostname":"{{AppHost}}"}""");
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_HostnameDiffersOnlyInCase_ReturnsSuccess()
    {
        var sut = Create("""{"success":true,"hostname":"ORKYO.Example"}""");
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeTrue();
    }

    // ── VerifyAsync: hostname ─────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_TokenIssuedForAnotherHostname_ReturnsFailure()
    {
        var sut = Create("""{"success":true,"hostname":"other.example"}""");
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("hostname_mismatch");
    }

    [Fact]
    public async Task VerifyAsync_SuccessWithoutHostname_ReturnsFailure()
    {
        var sut = Create("""{"success":true}""");
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("hostname_mismatch");
    }

    [Fact]
    public async Task VerifyAsync_HostnameMismatchInDevelopment_StillReturnsFailure()
    {
        var sut = Create("""{"success":true,"hostname":"other.example"}""", EnvironmentNames.Development);
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeFalse();
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

    // ── VerifyAsync: verification unavailable ─────────────────────────────────

    [Fact]
    public async Task VerifyAsync_HttpClientThrowsOutsideDevelopment_ReturnsFailure()
    {
        var sut = Create(ThrowingClient());
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("verification_unavailable");
    }

    [Fact]
    public async Task VerifyAsync_HttpClientThrowsInDevelopment_PassesWithMarkerCode()
    {
        var sut = Create(ThrowingClient(), EnvironmentNames.Development);
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().Be("verification_unavailable");
    }

    // A rejection outside Development is an Error, so the daily report's error count shows a
    // verification outage that is turning real visitors away.
    [Theory]
    [InlineData(EnvironmentNames.Production, LogLevel.Error)]
    [InlineData(EnvironmentNames.Development, LogLevel.Warning)]
    public async Task VerifyAsync_HttpClientThrows_LogsAtTheLevelOfItsOutcome(string environment, LogLevel expected)
    {
        var logger = new Mock<ILogger<CloudflareTurnstileProvider>>();
        var sut = Create(ThrowingClient(), environment, logger.Object);

        await sut.VerifyAsync("token", "1.2.3.4");

        logger.Verify(l => l.Log(
            expected,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_NonSuccessStatusOutsideDevelopment_ReturnsFailure()
    {
        var sut = Create(FakeClient($$"""{"success":true,"hostname":"{{AppHost}}"}""", HttpStatusCode.InternalServerError));
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("verification_unavailable");
    }

    [Fact]
    public async Task VerifyAsync_MalformedJsonOutsideDevelopment_ReturnsFailure()
    {
        var sut = Create("not-json");
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("verification_unavailable");
    }

    [Fact]
    public async Task VerifyAsync_ResponseWithoutSuccessFieldOutsideDevelopment_ReturnsFailure()
    {
        var sut = Create("""{"error-codes":["internal-error"]}""");
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("verification_unavailable");
    }

    [Fact]
    public async Task VerifyAsync_ResponseWithoutSuccessFieldInDevelopment_PassesWithMarkerCode()
    {
        var sut = Create("""{}""", EnvironmentNames.Development);
        var result = await sut.VerifyAsync("token", "1.2.3.4");
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().Be("verification_unavailable");
    }
}
