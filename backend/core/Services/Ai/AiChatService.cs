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

    /// <summary>
    /// The site the person is looking at, when they are looking at one. Carries the zone
    /// their "tomorrow" is measured in — see <see cref="AiSystemPrompt.Dynamic"/>.
    /// </summary>
    public Guid? SiteId { get; init; }
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

    /// <summary>
    /// Take the person somewhere in the app. Unlike a proposal this is not a request for
    /// permission — opening a view touches no workspace data — so the turn continues.
    /// </summary>
    public sealed record UiAction(string View, string? EntityId, string? SiteId) : AiChatEvent;

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
    ISchedulingService scheduling,
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

        // Both ceilings, not just the count. A conversation can reach the byte limit long
        // before the message limit — a few tool results carrying dozens of records will do
        // it — and MaxTranscriptBytes went unenforced until conversations became
        // persistent, when an oversized one would have survived every reload.
        if (request.Transcript.Count > AiDefaults.MaxTranscriptMessages
            || TranscriptBytes(request.Transcript) > AiDefaults.MaxTranscriptBytes)
        {
            yield return new AiChatEvent.Error("conversation_too_long",
                "This conversation has grown too long. Start a new one to continue.");
            yield return new AiChatEvent.Done();
            yield break;
        }

        // Counted once the turn can actually reach the provider, and before anything is
        // spent: a turn that fails on the way still used one, and counting on success
        // would let a failing turn be retried without end. Deliberately below the
        // transcript check — a conversation refused for its size never reaches Anthropic,
        // so charging for it would take a daily interaction and give nothing back.
        //
        // The count is not atomic with the check in EvaluateAsync above. Turns already in
        // flight can push it past the limit, so treat the ceiling as a damper on spend
        // rather than an exact quota.
        await access.RecordDailyTurnAsync(token);

        var apiKey = await credentials.GetApiKeyAsync(token);
        if (string.IsNullOrEmpty(apiKey))
        {
            yield return new AiChatEvent.Error("not_configured",
                "This workspace has no AI key configured. An administrator can add one in Administration.");
            yield return new AiChatEvent.Done();
            yield break;
        }

        var model = await credentials.GetModelAsync(token);

        // Sites own their working hours and the zone those hours are written in, so a
        // person's "tomorrow morning" is their site's, not UTC's. Best-effort: a turn is
        // still worth having without it, just less precise about time.
        string? siteTimeZone = null;
        if (request.SiteId is { } siteId)
        {
            try
            {
                siteTimeZone = (await scheduling.GetSettingsAsync(siteId, token))?.TimeZone;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read the time zone for site {SiteId}", siteId);
            }
        }

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
                    DynamicSystemPrompt = AiSystemPrompt.Dynamic(authorization.CanEdit, siteTimeZone: siteTimeZone),
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
                    // Opening a view happens in the browser, so there is nothing to run
                    // here. Emit the event, answer the model, and carry on — the assistant
                    // can show something and keep talking within the same turn.
                    if (AiUiTools.IsUiAction(call.Name ?? ""))
                    {
                        var (uiEvent, resultText) = ResolveUiAction(call);
                        if (uiEvent is not null) yield return uiEvent;
                        results.Add(AiBlock.ToolResult(
                            call.ToolUseId ?? "", resultText, isError: uiEvent is null));
                        continue;
                    }

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
                    await access.RecordUsageAsync(inputTokens, outputTokens, CancellationToken.None);
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

    /// <summary>
    /// What the transcript will weigh on the wire. Measured on the serialized form
    /// because that is what the ceiling is about — the payload, not the object graph.
    /// </summary>
    private static int TranscriptBytes(IReadOnlyList<AiMessage> transcript)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(transcript));
        }
        catch (NotSupportedException)
        {
            // Unserializable means unsendable; let the turn refuse it as oversized rather
            // than fail later inside the provider call.
            return int.MaxValue;
        }
    }

    /// <summary>
    /// Checks an <c>open_view</c> call against the catalog and this person's role, and says
    /// what the model should be told. Returns a null event when the call is refused — the
    /// model still gets an answer, so the turn continues instead of stalling.
    ///
    /// Validation repeats here rather than trusting the enum in the tool definition: the
    /// definition is guidance to a model, not a guarantee about what arrives.
    /// </summary>
    private (AiChatEvent.UiAction? Event, string Result) ResolveUiAction(AiBlock call)
    {
        string? viewId, entityId, siteId;
        try
        {
            using var document = JsonDocument.Parse(call.InputJson ?? "{}");
            viewId = AiToolInput.String(document.RootElement, "view");
            entityId = AiToolInput.String(document.RootElement, "entityId");
            siteId = AiToolInput.String(document.RootElement, "siteId");
        }
        catch (JsonException)
        {
            return (null, "That call was not valid JSON, so nothing opened.");
        }

        if (AiViewCatalog.Find(viewId) is not { } view)
            return (null, $"There is no view called '{viewId}'. Nothing opened. Pick one from the list in the tool description.");

        if (!AiViewCatalog.IsAllowed(view, authorization.CanEdit, authorization.IsAdmin))
            return (null, $"This person's role does not include '{view.Id}', so it did not open. Do not offer it to them.");

        if (view.NeedsEntityId && !Guid.TryParse(entityId, out _))
            return (null, $"'{view.Id}' opens one record and needs its id, which was missing or not an id. Nothing opened.");

        // The client persists this as the selected site, so an invented value would strand
        // the person on a site that does not exist. entityId is already parsed; this was not.
        var checkedSiteId = Guid.TryParse(siteId, out var parsedSite) ? parsedSite.ToString() : null;

        var opened = new AiChatEvent.UiAction(view.Id, view.NeedsEntityId ? entityId : null, checkedSiteId);
        return (opened, $"The app is opening '{view.Id}' for the person now. Tell them what they are looking at.");
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
        // Everyone may be shown around; the enum inside the definition is what varies by role.
        definitions.Add(AiUiTools.DefinitionFor(authorization.CanEdit, authorization.IsAdmin));
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
        "daily_limit_reached" => "You have used your AI interactions for today. Your allowance returns tomorrow.",
        "workspace_daily_limit_reached" => "This workspace has used its AI interactions for today. The allowance returns tomorrow, or an administrator can raise the daily limit.",
        _ => "The AI assistant is not available.",
    };
}
