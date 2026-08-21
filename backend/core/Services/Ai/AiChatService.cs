using System.Text.Json;
using Api.Security;

namespace Api.Services.Ai;

/// <summary>What the client sends to run one turn.</summary>
public sealed record AiChatRequest
{
    /// <summary>The person's message. Null when the turn only reports a proposal outcome.</summary>
    public string? Message { get; init; }

    /// <summary>The conversation so far, as returned by the previous turn.</summary>
    public IReadOnlyList<AiMessage> Transcript { get; init; } = [];

    /// <summary>Set when the assistant was opened from a conflict.</summary>
    public Guid? ContextRequestId { get; init; }
    public string? ContextConflictKind { get; init; }

    /// <summary>Set when the person accepted or declined a proposal from the previous turn.</summary>
    public AiProposalOutcome? PendingToolResult { get; init; }
}

/// <summary>What became of a proposal after the person decided.</summary>
public sealed record AiProposalOutcome
{
    public required string ToolUseId { get; init; }
    /// <summary>One of: <c>applied</c>, <c>declined</c>, <c>failed</c>.</summary>
    public required string Status { get; init; }
    public string? Detail { get; init; }
}

/// <summary>Something the assistant wants to do, waiting on the person to confirm it.</summary>
public sealed record AiProposal
{
    public required string ToolUseId { get; init; }
    public required string Kind { get; init; }
    public required string InputJson { get; init; }
}

/// <summary>One thing that happened during a turn, streamed to the client as it happens.</summary>
public abstract record AiChatEvent
{
    /// <summary>The assistant is working — which tool, so the panel can say so.</summary>
    public sealed record Status(string Phase, string? Tool = null) : AiChatEvent;

    /// <summary>Assistant prose.</summary>
    public sealed record Message(string Text) : AiChatEvent;

    /// <summary>A change awaiting confirmation. The turn ends here.</summary>
    public sealed record Proposal(AiProposal Value) : AiChatEvent;

    /// <summary>The conversation state to send back next turn.</summary>
    public sealed record Transcript(IReadOnlyList<AiMessage> Messages) : AiChatEvent;

    /// <summary>The turn failed. <paramref name="Code"/> is stable; the UI branches on it.</summary>
    public sealed record Error(string Code, string Detail) : AiChatEvent;

    public sealed record Done : AiChatEvent;
}

public interface IAiChatService
{
    /// <summary>
    /// Runs one turn, yielding events as they happen. Enforces access and budget before
    /// the first provider call and records the turn's token spend after the last one.
    /// </summary>
    IAsyncEnumerable<AiChatEvent> RunTurnAsync(AiChatRequest request, CancellationToken ct);
}

/// <summary>
/// The assistant's turn loop: ask the model, run any read-only tools it asks for, repeat
/// until it answers in prose, it proposes a change, or the iteration cap is reached.
///
/// The loop deliberately owns its own control flow rather than using an SDK tool runner,
/// because "a proposal ends the turn and hands control back to the person" is the
/// central rule here, not an interception hook.
/// </summary>
public sealed class AiChatService(
    IAnthropicGateway gateway,
    IAiCredentialService credentials,
    IAiAccessService access,
    IEnumerable<IAiTool> tools,
    IAuthorizationContext authorization,
    ICurrentPrincipal principal,
    ILogger<AiChatService> logger) : IAiChatService
{
    public async IAsyncEnumerable<AiChatEvent> RunTurnAsync(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // A turn stops when the caller hangs up or when it has simply run too long —
        // there is no point spending tokens on an answer nobody will read.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(AiDefaults.TurnDeadline);
        var token = deadline.Token;

        var decision = await access.EvaluateAsync(token);
        if (!decision.Allowed)
        {
            yield return new AiChatEvent.Error(decision.Reason ?? "not_allowed", DescribeDenial(decision.Reason));
            yield return new AiChatEvent.Done();
            yield break;
        }

        if (request.Transcript.Count > AiDefaults.MaxTranscriptMessages)
        {
            yield return new AiChatEvent.Error("conversation_too_long",
                "This conversation has grown too long. Start a new one to continue.");
            yield return new AiChatEvent.Done();
            yield break;
        }

        var apiKey = await credentials.GetApiKeyAsync(token);
        if (string.IsNullOrEmpty(apiKey))
        {
            yield return new AiChatEvent.Error("not_configured",
                "This workspace has no AI key configured. An administrator can add one in Administration.");
            yield return new AiChatEvent.Done();
            yield break;
        }

        var model = await credentials.GetModelAsync(token);
        var toolset = tools.ToDictionary(t => t.Definition.Name, StringComparer.Ordinal);
        var definitions = BuildToolDefinitions(toolset);
        var messages = BuildOpeningMessages(request);

        long inputTokens = 0, outputTokens = 0;
        var completed = false;

        try
        {
            for (var iteration = 0; iteration <= AiDefaults.MaxToolIterations && !completed; iteration++)
            {
                yield return new AiChatEvent.Status("thinking");

                // The provider call is wrapped rather than caught inline, because a C#
                // iterator cannot yield from inside a catch block.
                var (maybeResponse, failure) = await TrySendAsync(new AiGatewayRequest
                {
                    ApiKey = apiKey,
                    Model = model,
                    StaticSystemPrompt = AiSystemPrompt.Static(),
                    DynamicSystemPrompt = AiSystemPrompt.Dynamic(authorization.CanEdit),
                    Messages = messages,
                    Tools = definitions,
                }, token);

                if (failure is not null || maybeResponse is null)
                {
                    yield return new AiChatEvent.Error(
                        failure?.Code ?? "upstream_error",
                        failure?.Message ?? "The AI service could not be reached.");
                    break;
                }

                var response = maybeResponse;

                inputTokens += response.InputTokens;
                outputTokens += response.OutputTokens;
                messages.Add(AiMessage.Assistant(response.Blocks));

                // Safety classifiers answer with HTTP 200 and no content, so the stop
                // reason has to be read before the blocks are.
                if (response.StopReason == "refusal")
                {
                    yield return new AiChatEvent.Error("refused",
                        "The AI service declined to answer that. Try rephrasing the question.");
                    break;
                }

                foreach (var text in response.Blocks
                             .Where(b => b.Type == AiBlock.BlockTypes.Text && !string.IsNullOrWhiteSpace(b.Text)))
                {
                    yield return new AiChatEvent.Message(text.Text!);
                }

                var toolCalls = response.Blocks.Where(b => b.Type == AiBlock.BlockTypes.ToolUse).ToList();

                if (toolCalls.Count == 0)
                {
                    if (response.StopReason == "max_tokens")
                        yield return new AiChatEvent.Message("(The answer was cut off because it grew too long.)");
                    completed = true;
                    break;
                }

                // A proposal ends the turn: the person decides, not the model.
                if (toolCalls.FirstOrDefault(c => AiProposalTools.IsProposal(c.Name ?? "")) is { } proposal)
                {
                    yield return new AiChatEvent.Proposal(new AiProposal
                    {
                        ToolUseId = proposal.ToolUseId ?? "",
                        Kind = proposal.Name ?? "",
                        InputJson = proposal.InputJson ?? "{}",
                    });
                    completed = true;
                    break;
                }

                var results = new List<AiBlock>(toolCalls.Count);
                foreach (var call in toolCalls)
                {
                    yield return new AiChatEvent.Status("tool", call.Name);
                    results.Add(await RunToolAsync(toolset, call, token));
                }

                messages.Add(new AiMessage { Role = AiMessage.Roles.User, Blocks = results });
            }

            if (!completed)
            {
                yield return new AiChatEvent.Message(
                    "I stopped after several look-ups without reaching an answer. Try narrowing the question.");
            }
        }
        finally
        {
            // Spend is recorded even when the turn errors or is abandoned: the tokens were
            // still bought. Best-effort — a bookkeeping failure must not mask the answer.
            if (inputTokens + outputTokens > 0)
            {
                try
                {
                    await access.RecordUsageAsync(principal.UserId, inputTokens, outputTokens, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not record AI usage for user {UserId}", principal.UserId);
                }
            }

            logger.LogInformation(
                "AI turn finished: input={InputTokens} output={OutputTokens} messages={MessageCount}",
                inputTokens, outputTokens, messages.Count);
        }

        yield return new AiChatEvent.Transcript(messages);
        yield return new AiChatEvent.Done();
    }

    /// <summary>
    /// Calls the provider, returning either a response or a failure. Exists because an
    /// iterator method cannot yield from a catch block, and the failure has to reach the
    /// client as an event rather than an unhandled exception mid-stream.
    /// </summary>
    private async Task<(AiGatewayResponse? Response, AiGatewayException? Failure)> TrySendAsync(
        AiGatewayRequest request, CancellationToken ct)
    {
        try
        {
            return (await gateway.SendAsync(request, ct), null);
        }
        catch (AiGatewayException ex)
        {
            return (null, ex);
        }
    }

    private async Task<AiBlock> RunToolAsync(
        IReadOnlyDictionary<string, IAiTool> toolset, AiBlock call, CancellationToken ct)
    {
        var name = call.Name ?? "";
        var toolUseId = call.ToolUseId ?? "";

        if (!toolset.TryGetValue(name, out var tool))
            return AiBlock.ToolResult(toolUseId, $"There is no tool called {name}.", isError: true);

        try
        {
            using var document = JsonDocument.Parse(call.InputJson ?? "{}");
            var result = await tool.ExecuteAsync(document.RootElement, ct);
            return AiBlock.ToolResult(toolUseId, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The model can recover from a failed tool, so the failure goes back as a
            // result rather than ending the turn. The message stays generic: exception
            // text can carry workspace data.
            logger.LogWarning(ex, "AI tool {Tool} failed", name);
            return AiBlock.ToolResult(toolUseId, $"The {name} look-up failed.", isError: true);
        }
    }

    /// <summary>
    /// Read tools always; propose tools only for someone who could apply the result.
    /// Offering a proposal to a viewer would produce advice they cannot act on.
    /// </summary>
    private IReadOnlyList<AiToolDefinition> BuildToolDefinitions(IReadOnlyDictionary<string, IAiTool> toolset)
    {
        var definitions = toolset.Values.Select(t => t.Definition).ToList();
        if (authorization.CanEdit) definitions.AddRange(AiProposalTools.Definitions);
        return definitions;
    }

    private static List<AiMessage> BuildOpeningMessages(AiChatRequest request)
    {
        var messages = request.Transcript.ToList();

        // A decided proposal closes the tool call the previous turn left open, so the
        // model can see what actually happened and verify it.
        if (request.PendingToolResult is { } outcome)
        {
            messages.Add(new AiMessage
            {
                Role = AiMessage.Roles.User,
                Blocks = [AiBlock.ToolResult(outcome.ToolUseId, DescribeOutcome(outcome))],
            });
        }

        var opening = new List<AiBlock>();

        if (messages.Count == 0 && request.ContextRequestId is { } requestId)
            opening.Add(AiBlock.TextBlock(AiSystemPrompt.ConflictSeed(requestId, request.ContextConflictKind)));

        if (!string.IsNullOrWhiteSpace(request.Message))
            opening.Add(AiBlock.TextBlock(request.Message));

        if (opening.Count > 0)
            messages.Add(new AiMessage { Role = AiMessage.Roles.User, Blocks = opening });

        return messages;
    }

    private static string DescribeOutcome(AiProposalOutcome outcome) => outcome.Status switch
    {
        "applied" => $"The person applied this change.{Suffix(outcome.Detail)} Confirm the result before saying it is fixed.",
        "declined" => $"The person declined this change.{Suffix(outcome.Detail)} Do not propose the same thing again without a reason.",
        _ => $"The change failed to apply.{Suffix(outcome.Detail)}",
    };

    private static string Suffix(string? detail) => string.IsNullOrWhiteSpace(detail) ? "" : $" {detail}";

    private static string DescribeDenial(string? reason) => reason switch
    {
        "not_entitled" => "The AI assistant is not included in this workspace's plan.",
        "not_configured" => "This workspace has no AI key configured. An administrator can add one in Administration.",
        "not_allowed" => "You do not have access to the AI assistant. An administrator can grant it in Administration.",
        "allowance_exhausted" => "You have used your AI token allowance for this month.",
        _ => "The AI assistant is not available.",
    };
}
