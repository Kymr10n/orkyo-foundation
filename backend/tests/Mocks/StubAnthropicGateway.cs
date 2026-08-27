using Api.Models;
using Api.Services.Ai;

namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// Stub for <see cref="IAnthropicGateway"/>.
///
/// The real gateway opens an outbound HTTPS connection to Anthropic, which an integration
/// test must never do: it needs a live key, it costs money, and it makes the suite depend
/// on somebody else's uptime. Tests set <see cref="TestResult"/> to choose what a key
/// probe reports.
/// </summary>
public sealed class StubAnthropicGateway : IAnthropicGateway
{
    /// <summary>What <see cref="TestAsync"/> reports back to the endpoint.</summary>
    public AiCredentialTestResult TestResult { get; set; } =
        new() { Ok = false, Reason = "stub_not_configured" };

    /// <summary>How many times an endpoint probed the stored key.</summary>
    public int TestCallCount { get; private set; }

    public Task<AiCredentialTestResult> TestAsync(
        string apiKey, string model, CancellationToken ct = default)
    {
        TestCallCount++;
        return Task.FromResult(TestResult);
    }

    public Task<AiGatewayResponse> SendAsync(AiGatewayRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "The assistant's chat turn is covered by AiChatServiceTests, which drives the stream directly.");
}
