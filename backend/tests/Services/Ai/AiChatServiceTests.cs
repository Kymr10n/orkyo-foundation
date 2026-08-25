using System.Text.Json;
using Api.Models;
using Api.Security;
using Api.Services;
using Api.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkyo.Foundation.Tests.Services.Ai;

/// <summary>
/// The turn loop, driven by a scripted gateway so no network is involved. These pin the
/// behaviours that make the assistant safe rather than merely working: a proposal stops
/// the loop instead of acting, a refusal is surfaced rather than read as content, tool
/// failures do not end the turn, and spend is always recorded.
/// </summary>
public class AiChatServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly FakeAnthropicGateway _gateway = new();
    private readonly Mock<IAiCredentialService> _credentials = new();
    private readonly Mock<IAiAccessService> _access = new();
    private readonly Mock<IAuthorizationContext> _authorization = new();
    private readonly Mock<ICurrentPrincipal> _principal = new();
    private readonly FakeTool _tool = new();
    private readonly Mock<ISchedulingService> _scheduling = new();

    public AiChatServiceTests()
    {
        _credentials.Setup(c => c.GetApiKeyAsync(It.IsAny<CancellationToken>())).ReturnsAsync("sk-ant-test-key");
        _credentials.Setup(c => c.GetModelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AiDefaults.Model);
        _access.Setup(a => a.EvaluateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAccessDecision { Allowed = true });
        _authorization.SetupGet(a => a.CanEdit).Returns(true);
        _principal.SetupGet(p => p.UserId).Returns(UserId);
    }

    private AiChatService CreateSut() => new(
        _gateway, _credentials.Object, _access.Object, [_tool],
        _authorization.Object, _principal.Object, _scheduling.Object,
        NullLogger<AiChatService>.Instance);

    private async Task<List<AiChatEvent>> RunAsync(AiChatRequest? request = null)
    {
        var events = new List<AiChatEvent>();
        await foreach (var e in CreateSut().RunTurnAsync(request ?? new AiChatRequest { Message = "hello" }, default))
            events.Add(e);
        return events;
    }

    [Fact]
    public async Task PlainAnswer_EndsTheTurn()
    {
        _gateway.Enqueue(Reply(AiBlock.TextBlock("Three requests overlap on Studio A.")));

        var events = await RunAsync();

        events.OfType<AiChatEvent.Message>().Single().Text.Should().Contain("Studio A");
        events.Last().Should().BeOfType<AiChatEvent.Done>();
        _gateway.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ToolCall_RunsTheToolAndContinues()
    {
        _gateway.Enqueue(Reply(ToolUse("toolu_1", _tool.Definition.Name, "{}")));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("Done.")));

        var events = await RunAsync();

        _tool.Executions.Should().Be(1);
        events.OfType<AiChatEvent.Status>().Should().Contain(s => s.Tool == _tool.Definition.Name);
        _gateway.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ASiteOnScreenPutsItsZoneInTheTurn()
    {
        _scheduling
            .Setup(s => s.GetSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SchedulingSettingsInfo.Default(Guid.NewGuid()) with { TimeZone = "Europe/Berlin" });
        _gateway.Enqueue(Reply(AiBlock.TextBlock("ok")));

        await RunAsync(new AiChatRequest { Message = "tomorrow morning?", SiteId = Guid.NewGuid() });

        _gateway.LastRequest!.DynamicSystemPrompt.Should().Contain("Europe/Berlin");
    }

    [Fact]
    public async Task NoSiteMeansTheTurnStaysOnUtc()
    {
        _gateway.Enqueue(Reply(AiBlock.TextBlock("ok")));

        await RunAsync(new AiChatRequest { Message = "hello" });

        _gateway.LastRequest!.DynamicSystemPrompt.Should().NotContain("keeps time in");
        _scheduling.Verify(s => s.GetSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnreadableZoneDoesNotCostThePersonTheirAnswer()
    {
        // Time precision is worth less than the turn itself.
        _scheduling
            .Setup(s => s.GetSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("settings unavailable"));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("still answered")));

        var events = await RunAsync(new AiChatRequest { Message = "hi", SiteId = Guid.NewGuid() });

        events.OfType<AiChatEvent.Message>().Single().Text.Should().Contain("still answered");
    }

    [Fact]
    public async Task AnOversizedTranscriptIsRefusedOnBytesNotJustCount()
    {
        // A handful of tool results carrying dozens of records reaches the byte ceiling
        // long before the forty-message one. MaxTranscriptBytes existed but was enforced
        // nowhere until conversations became persistent.
        var bulky = new AiMessage
        {
            Role = AiMessage.Roles.User,
            Blocks = [AiBlock.TextBlock(new string('x', AiDefaults.MaxTranscriptBytes))],
        };

        var events = await RunAsync(new AiChatRequest { Message = "and now?", Transcript = [bulky] });

        var error = events.OfType<AiChatEvent.Error>().Single();
        error.Code.Should().Be("conversation_too_long");
        _gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task OpenView_EmitsTheActionAndKeepsTalking()
    {
        // Unlike a proposal, opening a view is not a request for permission — it touches no
        // workspace data — so the turn must continue and let the model explain what the
        // person is now looking at.
        _gateway.Enqueue(Reply(ToolUse("toolu_v", AiUiTools.OpenView, """{"view":"insights_conflicts"}""")));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("The red bars are the overbooked weeks.")));

        var events = await RunAsync();

        var opened = events.OfType<AiChatEvent.UiAction>().Single();
        opened.View.Should().Be("insights_conflicts");
        opened.EntityId.Should().BeNull();
        events.OfType<AiChatEvent.Message>().Single().Text.Should().Contain("red bars");
        _gateway.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task OpenView_AnswersTheModelSoTheConversationCanContinue()
    {
        _gateway.Enqueue(Reply(ToolUse("toolu_v", AiUiTools.OpenView, """{"view":"requests"}""")));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("done")));

        await RunAsync();

        // Every tool_use needs its tool_result, or the next call is malformed.
        var results = _gateway.LastRequest!.Messages
            .SelectMany(m => m.Blocks)
            .Where(b => b.Type == AiBlock.BlockTypes.ToolResult)
            .ToList();
        results.Should().ContainSingle(b => b.ToolUseId == "toolu_v");
    }

    [Fact]
    public async Task OpenView_RefusesAnInventedViewWithoutMovingAnyone()
    {
        _gateway.Enqueue(Reply(ToolUse("toolu_v", AiUiTools.OpenView, """{"view":"/etc/passwd"}""")));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("sorry")));

        var events = await RunAsync();

        events.OfType<AiChatEvent.UiAction>().Should().BeEmpty();
        // The model is still told, so it can apologise rather than hang.
        _gateway.LastRequest!.Messages
            .SelectMany(m => m.Blocks)
            .Should().Contain(b => b.ToolUseId == "toolu_v" && b.Content != null && b.Content.Contains("no view called"));
    }

    [Fact]
    public async Task OpenView_RefusesARecordViewWithNoRecord()
    {
        _gateway.Enqueue(Reply(ToolUse("toolu_v", AiUiTools.OpenView, """{"view":"request"}""")));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("sorry")));

        var events = await RunAsync();

        events.OfType<AiChatEvent.UiAction>().Should().BeEmpty();
    }

    [Fact]
    public async Task OpenView_CarriesTheRecordId()
    {
        var id = Guid.NewGuid();
        _gateway.Enqueue(Reply(ToolUse("toolu_v", AiUiTools.OpenView, $$"""{"view":"request","entityId":"{{id}}"}""")));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("opened")));

        var events = await RunAsync();

        events.OfType<AiChatEvent.UiAction>().Single().EntityId.Should().Be(id.ToString());
    }

    [Fact]
    public async Task OpenView_RefusesAViewTheRoleDoesNotCover()
    {
        // A viewer is never offered admin ids in the enum, but the enum is guidance to a
        // model, not a guarantee about what arrives.
        _authorization.SetupGet(a => a.CanEdit).Returns(false);
        _authorization.SetupGet(a => a.IsAdmin).Returns(false);
        _gateway.Enqueue(Reply(ToolUse("toolu_v", AiUiTools.OpenView, """{"view":"admin_users"}""")));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("sorry")));

        var events = await RunAsync();

        events.OfType<AiChatEvent.UiAction>().Should().BeEmpty();
    }

    [Fact]
    public async Task OpenView_DoesNotSwallowASiblingRead()
    {
        // The proposal branch takes the first proposal and drops its siblings. A view must
        // not behave that way: the read the model asked for in the same block still runs.
        _gateway.Enqueue(new AiGatewayResponse
        {
            Blocks =
            [
                ToolUse("toolu_v", AiUiTools.OpenView, """{"view":"requests"}"""),
                ToolUse("toolu_t", _tool.Definition.Name, "{}"),
            ],
            StopReason = "tool_use",
        });
        _gateway.Enqueue(Reply(AiBlock.TextBlock("both done")));

        var events = await RunAsync();

        events.OfType<AiChatEvent.UiAction>().Should().ContainSingle();
        _tool.Executions.Should().Be(1);
    }

    [Fact]
    public async Task Proposal_StopsTheLoopAndIsNotExecuted()
    {
        _gateway.Enqueue(Reply(ToolUse("toolu_9", AiProposalTools.ProposeUpdateRequest,
            """{"requestId":"a","changes":{"startTs":"2026-01-01T09:00:00Z"},"rationale":"frees Studio A"}""")));
        // If the loop kept going it would consume this, and the test would see two calls.
        _gateway.Enqueue(Reply(AiBlock.TextBlock("should never be reached")));

        var events = await RunAsync();

        var proposal = events.OfType<AiChatEvent.Proposal>().Single();
        proposal.Value.Kind.Should().Be(AiProposalTools.ProposeUpdateRequest);
        proposal.Value.ToolUseId.Should().Be("toolu_9");
        _gateway.CallCount.Should().Be(1);
        _tool.Executions.Should().Be(0);
    }

    [Fact]
    public async Task Refusal_IsReportedRatherThanTreatedAsAnAnswer()
    {
        _gateway.Enqueue(new AiGatewayResponse { Blocks = [], StopReason = "refusal", InputTokens = 5, OutputTokens = 0 });

        var events = await RunAsync();

        events.OfType<AiChatEvent.Error>().Single().Code.Should().Be("refused");
    }

    [Fact]
    public async Task ToolFailure_ComesBackAsAResultSoTheModelCanRecover()
    {
        _tool.ThrowOnExecute = true;
        _gateway.Enqueue(Reply(ToolUse("toolu_2", _tool.Definition.Name, "{}")));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("I could not read that.")));

        var events = await RunAsync();

        var sent = _gateway.LastRequest!.Messages;
        var result = sent.SelectMany(m => m.Blocks).Single(b => b.Type == AiBlock.BlockTypes.ToolResult);
        result.IsError.Should().BeTrue();
        events.Last().Should().BeOfType<AiChatEvent.Done>();
    }

    [Fact]
    public async Task IterationCap_StopsAnEndlessToolLoop()
    {
        // Always asks for a tool, never answers.
        for (var i = 0; i < AiDefaults.MaxToolIterations + 5; i++)
            _gateway.Enqueue(Reply(ToolUse($"toolu_{i}", _tool.Definition.Name, "{}")));

        var events = await RunAsync();

        _gateway.CallCount.Should().BeLessThanOrEqualTo(AiDefaults.MaxToolIterations + 1);
        events.OfType<AiChatEvent.Message>().Last().Text.Should().Contain("stopped");
    }

    [Fact]
    public async Task ThinkingBlocks_AreEchoedBackUnchanged()
    {
        // The provider rejects a modified thinking block, so the signature has to survive
        // the round-trip through Orkyo's own transcript shape.
        var thinking = new AiBlock
        {
            Type = AiBlock.BlockTypes.Thinking,
            Thinking = "",
            Signature = "sig-abc",
        };
        _gateway.Enqueue(Reply(thinking, ToolUse("toolu_3", _tool.Definition.Name, "{}")));
        _gateway.Enqueue(Reply(AiBlock.TextBlock("done")));

        await RunAsync();

        var echoed = _gateway.LastRequest!.Messages
            .SelectMany(m => m.Blocks)
            .Single(b => b.Type == AiBlock.BlockTypes.Thinking);
        echoed.Signature.Should().Be("sig-abc");
    }

    [Fact]
    public async Task Usage_IsRecordedEvenWhenTheTurnFails()
    {
        _gateway.Enqueue(new AiGatewayResponse
        {
            Blocks = [],
            StopReason = "refusal",
            InputTokens = 120,
            OutputTokens = 34,
        });

        await RunAsync();

        _access.Verify(a => a.RecordUsageAsync(120, 34, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeniedCaller_NeverReachesTheProvider()
    {
        _access.Setup(a => a.EvaluateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiAccessDecision.Deny("allowance_exhausted"));

        var events = await RunAsync();

        events.OfType<AiChatEvent.Error>().Single().Code.Should().Be("allowance_exhausted");
        _gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MissingKey_IsReportedWithoutCallingTheProvider()
    {
        _credentials.Setup(c => c.GetApiKeyAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var events = await RunAsync();

        events.OfType<AiChatEvent.Error>().Single().Code.Should().Be("not_configured");
        _gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Viewer_IsNotOfferedProposalTools()
    {
        _authorization.SetupGet(a => a.CanEdit).Returns(false);
        _gateway.Enqueue(Reply(AiBlock.TextBlock("Here is what is wrong.")));

        await RunAsync();

        _gateway.LastRequest!.Tools.Select(t => t.Name)
            .Should().NotContain(AiProposalTools.ProposeUpdateRequest);
    }

    [Fact]
    public async Task Editor_IsOfferedProposalTools()
    {
        _gateway.Enqueue(Reply(AiBlock.TextBlock("Here is what is wrong.")));

        await RunAsync();

        _gateway.LastRequest!.Tools.Select(t => t.Name)
            .Should().Contain(AiProposalTools.ProposeUpdateRequest);
    }

    [Fact]
    public async Task OverlongTranscript_IsRefusedBeforeSpendingAnything()
    {
        var transcript = Enumerable.Range(0, AiDefaults.MaxTranscriptMessages + 1)
            .Select(_ => AiMessage.User(AiBlock.TextBlock("x")))
            .ToList();

        var events = await RunAsync(new AiChatRequest { Message = "hi", Transcript = transcript });

        events.OfType<AiChatEvent.Error>().Single().Code.Should().Be("conversation_too_long");
        _gateway.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DecidedProposal_ClosesTheOpenToolCall()
    {
        _gateway.Enqueue(Reply(AiBlock.TextBlock("Confirmed — the overlap is gone.")));

        await RunAsync(new AiChatRequest
        {
            Transcript = [AiMessage.Assistant([ToolUse("toolu_7", AiProposalTools.ProposeUpdateRequest, "{}")])],
            PendingToolResult = new AiProposalOutcome { ToolUseId = "toolu_7", Status = "applied" },
        });

        var result = _gateway.LastRequest!.Messages
            .SelectMany(m => m.Blocks)
            .Single(b => b.Type == AiBlock.BlockTypes.ToolResult);
        result.ToolUseId.Should().Be("toolu_7");
        result.Content.Should().Contain("applied");
    }

    private static AiGatewayResponse Reply(params AiBlock[] blocks) => new()
    {
        Blocks = blocks,
        StopReason = blocks.Any(b => b.Type == AiBlock.BlockTypes.ToolUse) ? "tool_use" : "end_turn",
        InputTokens = 10,
        OutputTokens = 5,
    };

    private static AiBlock ToolUse(string id, string name, string inputJson) => new()
    {
        Type = AiBlock.BlockTypes.ToolUse,
        ToolUseId = id,
        Name = name,
        InputJson = inputJson,
    };

    /// <summary>Replays a scripted sequence of provider responses and records what it was sent.</summary>
    private sealed class FakeAnthropicGateway : IAnthropicGateway
    {
        private readonly Queue<AiGatewayResponse> _responses = new();

        public int CallCount { get; private set; }
        public AiGatewayRequest? LastRequest { get; private set; }

        public void Enqueue(AiGatewayResponse response) => _responses.Enqueue(response);

        public Task<AiGatewayResponse> SendAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new AiGatewayResponse { Blocks = [], StopReason = "end_turn" });
        }

        public Task<AiCredentialTestResult> TestAsync(string apiKey, string model, CancellationToken ct = default) =>
            Task.FromResult(new AiCredentialTestResult { Ok = true });
    }

    private sealed class FakeTool : IAiTool
    {
        public int Executions { get; private set; }
        public bool ThrowOnExecute { get; set; }

        public AiToolDefinition Definition { get; } = new()
        {
            Name = "fake_tool",
            Description = "Test double.",
            InputSchemaJson = """{"type":"object","properties":{}}""",
        };

        public Task<string> ExecuteAsync(JsonElement input, CancellationToken ct)
        {
            Executions++;
            if (ThrowOnExecute) throw new InvalidOperationException("boom");
            return Task.FromResult("ok");
        }
    }
}
