using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Api.Models;

namespace Api.Services.Ai;

/// <summary>
/// The seam between Orkyo and the model provider. Everything above this interface speaks
/// <see cref="AiBlock"/> and <see cref="AiToolDefinition"/>, so the chat loop, the tools,
/// and the endpoint carry no provider types and are testable with a fake.
/// </summary>
public interface IAnthropicGateway
{
    Task<AiGatewayResponse> SendAsync(AiGatewayRequest request, CancellationToken ct = default);

    /// <summary>Cheapest possible proof that a key works: retrieve the model, spend no tokens.</summary>
    Task<AiCredentialTestResult> TestAsync(string apiKey, string model, CancellationToken ct = default);
}

/// <summary>
/// Anthropic implementation. The client is constructed per call from the workspace's own
/// key — there is no shared authenticated client, because the credential differs per
/// workspace and must never outlive the request that fetched it.
/// <para>
/// The <em>transport</em> is shared even though the client is not. Each call wraps the
/// static handler in a throwaway <see cref="HttpClient"/> that does not own it, so every
/// workspace draws from one connection pool instead of opening a fresh socket per turn.
/// Wrapping rather than sharing the <see cref="HttpClient"/> itself keeps this correct
/// whether or not the SDK disposes the instance it is handed.
/// </para>
/// </summary>
public sealed class AnthropicGateway(ILogger<AnthropicGateway> logger) : IAnthropicGateway
{
    /// <summary>
    /// One connection pool for the process. <see cref="SocketsHttpHandler.PooledConnectionLifetime"/>
    /// is set because a pool that never recycles keeps connections open across DNS changes.
    /// </summary>
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>A non-owning view over <see cref="SharedHandler"/>: disposing it leaves the pool intact.</summary>
    private static HttpClient BorrowTransport() => new(SharedHandler, disposeHandler: false);

    public async Task<AiGatewayResponse> SendAsync(AiGatewayRequest request, CancellationToken ct = default)
    {
        var client = new AnthropicClient { ApiKey = request.ApiKey, HttpClient = BorrowTransport() };

        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            // Stable instructions first with a cache breakpoint, volatile context after it,
            // so repeated turns and tool hops read the prefix from cache instead of paying
            // for it again. See shared/prompt-caching.md.
            System = new List<TextBlockParam>
            {
                new() { Text = request.StaticSystemPrompt, CacheControl = new CacheControlEphemeral() },
                new() { Text = request.DynamicSystemPrompt },
            },
            Messages = request.Messages.Select(ToMessageParam).ToList(),
            Tools = request.Tools.Select(ToToolUnion).ToList(),
        };

        Message response;
        try
        {
            response = await client.Messages.Create(parameters, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            throw; // The caller hung up or the turn deadline passed — not an upstream fault.
        }
        catch (Exception ex)
        {
            throw TranslateFailure(ex);
        }

        return new AiGatewayResponse
        {
            Blocks = response.Content.Select(FromContentBlock).Where(b => b is not null).Select(b => b!).ToList(),
            StopReason = response.StopReason?.ToString() ?? "end_turn",
            InputTokens = response.Usage?.InputTokens ?? 0,
            OutputTokens = response.Usage?.OutputTokens ?? 0,
        };
    }

    public async Task<AiCredentialTestResult> TestAsync(string apiKey, string model, CancellationToken ct = default)
    {
        try
        {
            var client = new AnthropicClient { ApiKey = apiKey, HttpClient = BorrowTransport() };
            await client.Models.Retrieve(model, cancellationToken: ct);
            return new AiCredentialTestResult { Ok = true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reason = ClassifyFailure(ex) switch
            {
                "credential_invalid" => "invalid_key",
                "upstream_busy" => "network",
                _ => ex.Message.Contains("model", StringComparison.OrdinalIgnoreCase)
                    ? "model_unavailable"
                    : "network",
            };
            // Deliberately coarse: an admin needs to know whether the key is wrong or the
            // network is, not the provider's internal error text.
            logger.LogWarning("AI credential probe failed: {Reason}", reason);
            return new AiCredentialTestResult { Ok = false, Reason = reason };
        }
    }

    private static MessageParam ToMessageParam(AiMessage message) => new()
    {
        Role = message.Role == AiMessage.Roles.Assistant ? Role.Assistant : Role.User,
        Content = message.Blocks.Select(ToContentBlockParam).ToList(),
    };

    private static ContentBlockParam ToContentBlockParam(AiBlock block) => block.Type switch
    {
        AiBlock.BlockTypes.Text => new TextBlockParam { Text = block.Text ?? "" },

        // Thinking blocks travel back exactly as they arrived, signature included. The
        // provider rejects a modified one, and an empty text body is still a valid block.
        AiBlock.BlockTypes.Thinking => new ThinkingBlockParam
        {
            Thinking = block.Thinking ?? "",
            Signature = block.Signature ?? "",
        },

        AiBlock.BlockTypes.ToolUse => new ToolUseBlockParam
        {
            ID = block.ToolUseId ?? "",
            Name = block.Name ?? "",
            Input = ParseInput(block.InputJson),
        },

        AiBlock.BlockTypes.ToolResult => new ToolResultBlockParam
        {
            ToolUseID = block.ToolUseId ?? "",
            Content = block.Content ?? "",
            IsError = block.IsError ?? false,
        },

        _ => new TextBlockParam { Text = block.Text ?? "" },
    };

    private static AiBlock? FromContentBlock(ContentBlock block)
    {
        if (block.TryPickText(out TextBlock? text))
            return new AiBlock { Type = AiBlock.BlockTypes.Text, Text = text!.Text };

        if (block.TryPickThinking(out ThinkingBlock? thinking))
            return new AiBlock
            {
                Type = AiBlock.BlockTypes.Thinking,
                Thinking = thinking!.Thinking,
                Signature = thinking.Signature,
            };

        if (block.TryPickToolUse(out ToolUseBlock? toolUse))
            return new AiBlock
            {
                Type = AiBlock.BlockTypes.ToolUse,
                ToolUseId = toolUse!.ID,
                Name = toolUse.Name,
                InputJson = JsonSerializer.Serialize(toolUse.Input),
            };

        // Anything else (server-tool blocks, future variants) is not part of this
        // conversation shape. Dropping it is safe: the loop only acts on the three above.
        return null;
    }

    private static ToolUnion ToToolUnion(AiToolDefinition definition)
    {
        using var document = JsonDocument.Parse(definition.InputSchemaJson);
        var schema = document.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        return new ToolUnion(new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = InputSchema.FromRawUnchecked(schema),
        });
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseInput(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return new Dictionary<string, JsonElement>();
        using var document = JsonDocument.Parse(inputJson);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private AiGatewayException TranslateFailure(Exception ex)
    {
        var code = ClassifyFailure(ex);
        // Log the class and the exception, never the request: prompts and tool results
        // carry workspace data, but the SDK's own error is diagnostic, not payload.
        logger.LogWarning(ex, "AI provider call failed with {Code}", code);

        return code switch
        {
            "credential_invalid" => new AiGatewayException(code,
                "The workspace's AI key was rejected. An administrator must check it in Administration.", ex),
            "upstream_busy" => new AiGatewayException(code,
                "The AI service is busy. Try again in a moment.", ex),
            _ => new AiGatewayException(code, "The AI service could not be reached.", ex),
        };
    }

    /// <summary>
    /// Maps a provider failure onto a stable code. Status is read off the exception text
    /// because the SDK's typed exceptions differ by transport, and the three classes we
    /// act on are distinguishable either way.
    /// </summary>
    private static string ClassifyFailure(Exception ex)
    {
        var message = ex.ToString();
        if (message.Contains("401") || message.Contains("authentication", StringComparison.OrdinalIgnoreCase))
            return "credential_invalid";
        if (message.Contains("429") || message.Contains("529") || message.Contains("overloaded", StringComparison.OrdinalIgnoreCase))
            return "upstream_busy";
        return "upstream_error";
    }
}
