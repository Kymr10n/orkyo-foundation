using Api.Models;
using Api.Services.Ai;

namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// Programmable stand-in for <see cref="IAnthropicGateway"/>, used both by the integration
/// host and by <c>AiChatServiceTests</c>.
///
/// The real gateway opens an outbound HTTPS connection to Anthropic, which a test must never
/// do: it needs a live key, it costs money, and it makes the suite depend on somebody else's
/// uptime. Tests set <see cref="TestResult"/> to choose what a key probe reports, and
/// <see cref="Enqueue"/> scripted provider responses for the chat turn; an empty queue
/// answers with an end-of-turn response carrying no blocks.
/// </summary>
public sealed class StubAnthropicGateway : IAnthropicGateway
{
    private readonly Queue<AiGatewayResponse> _responses = new();

    /// <summary>What <see cref="TestAsync"/> reports back to the endpoint.</summary>
    public AiCredentialTestResult TestResult { get; set; } =
        new() { Ok = false, Reason = "stub_not_configured" };

    /// <summary>How many times an endpoint probed the stored key.</summary>
    public int TestCallCount { get; private set; }

    /// <summary>How many chat turns were sent through <see cref="SendAsync"/>.</summary>
    public int CallCount { get; private set; }

    /// <summary>The most recent request handed to <see cref="SendAsync"/>.</summary>
    public AiGatewayRequest? LastRequest { get; private set; }

    /// <summary>Queues the next provider response; dequeued in order by <see cref="SendAsync"/>.</summary>
    public void Enqueue(AiGatewayResponse response) => _responses.Enqueue(response);

    public Task<AiCredentialTestResult> TestAsync(
        string apiKey, string model, CancellationToken ct = default)
    {
        TestCallCount++;
        return Task.FromResult(TestResult);
    }

    public Task<AiGatewayResponse> SendAsync(AiGatewayRequest request, CancellationToken ct = default)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(_responses.Count > 0
            ? _responses.Dequeue()
            : new AiGatewayResponse { Blocks = [], StopReason = "end_turn" });
    }
}
