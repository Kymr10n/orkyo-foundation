using System.Net;
using Anthropic.Exceptions;
using Api.Services.Ai;

namespace Orkyo.Foundation.Tests.Services.Ai;

/// <summary>
/// Failures are classified by the status the SDK's typed exception carries. The text used
/// to decide it: "401" anywhere in a message or stack trace read as a rejected key.
/// </summary>
public class AnthropicGatewayClassificationTests
{
    private static AnthropicApiException Status(HttpStatusCode status, string body = "") =>
        new AnthropicUnexpectedStatusCodeException(new HttpRequestException("upstream")) { StatusCode = status, ResponseBody = body };

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "credential_invalid")]
    [InlineData(HttpStatusCode.Forbidden, "credential_invalid")]
    [InlineData(HttpStatusCode.BadRequest, "upstream_error")]
    [InlineData(HttpStatusCode.TooManyRequests, "upstream_busy")]
    [InlineData((HttpStatusCode)529, "upstream_busy")]
    [InlineData(HttpStatusCode.InternalServerError, "upstream_error")]
    public void ClassifyFailure_ReadsTheStatus(HttpStatusCode status, string expected) =>
        AnthropicGateway.ClassifyFailure(Status(status)).Should().Be(expected);

    [Fact]
    public void ClassifyFailure_NoCreditLeft_IsACredentialProblem() =>
        AnthropicGateway.ClassifyFailure(Status(HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API."}}"""))
            .Should().Be("credential_invalid");

    [Theory]
    [InlineData("failed at line 401")]
    [InlineData("authentication handshake timed out")]
    [InlineData("server overloaded, 429 retries exhausted")]
    public void ClassifyFailure_IgnoresTheMessageText(string message) =>
        AnthropicGateway.ClassifyFailure(new HttpRequestException(message)).Should().Be("upstream_error");

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "invalid_key")]
    [InlineData(HttpStatusCode.NotFound, "model_unavailable")]
    [InlineData(HttpStatusCode.TooManyRequests, "network")]
    public void ClassifyProbeFailure_ReadsTheStatus(HttpStatusCode status, string expected) =>
        AnthropicGateway.ClassifyProbeFailure(Status(status)).Should().Be(expected);

    [Fact]
    public void ClassifyProbeFailure_ANetworkErrorMentioningTheModel_IsNetwork() =>
        AnthropicGateway.ClassifyProbeFailure(new HttpRequestException("could not reach model endpoint")).Should().Be("network");
}
