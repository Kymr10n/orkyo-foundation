namespace Api.Services.Ai;

/// <summary>
/// One block of a conversation, in Orkyo's own shape rather than the provider SDK's.
///
/// The conversation is stateless: the client holds the transcript and echoes it back on
/// every turn. That is only safe because nothing security-relevant travels in it — the
/// system prompt and the tool list are rebuilt server-side each turn, every tool is
/// read-only and runs under the caller's own workspace and role, and writes happen
/// through ordinary endpoints after a human confirms them. A tampered transcript can
/// mislead the model about what was said; it cannot widen what the caller may see or do.
///
/// One flat record with nullable members, rather than a polymorphic hierarchy, keeps the
/// wire contract stable and the JSON trivially serializable in both directions.
/// </summary>
public sealed record AiBlock
{
    /// <summary>One of: <c>text</c>, <c>thinking</c>, <c>tool_use</c>, <c>tool_result</c>.</summary>
    public required string Type { get; init; }

    public string? Text { get; init; }

    /// <summary>Summarized reasoning. Empty on models that omit it — the block still has to round-trip.</summary>
    public string? Thinking { get; init; }

    /// <summary>
    /// Opaque signature attached to a thinking block. Must be echoed back byte-for-byte:
    /// the provider rejects modified thinking blocks.
    /// </summary>
    public string? Signature { get; init; }

    public string? ToolUseId { get; init; }
    public string? Name { get; init; }

    /// <summary>Tool input as raw JSON, kept as text so the loop never has to model each tool's shape.</summary>
    public string? InputJson { get; init; }

    /// <summary>Tool result payload, already rendered to text.</summary>
    public string? Content { get; init; }

    public bool? IsError { get; init; }

    public static AiBlock TextBlock(string text) => new() { Type = BlockTypes.Text, Text = text };

    public static AiBlock ToolResult(string toolUseId, string content, bool isError = false) => new()
    {
        Type = BlockTypes.ToolResult,
        ToolUseId = toolUseId,
        Content = content,
        IsError = isError,
    };

    public static class BlockTypes
    {
        public const string Text = "text";
        public const string Thinking = "thinking";
        public const string ToolUse = "tool_use";
        public const string ToolResult = "tool_result";
    }
}

/// <summary>One conversation turn. <see cref="Role"/> is <c>user</c> or <c>assistant</c>.</summary>
public sealed record AiMessage
{
    public required string Role { get; init; }
    public required IReadOnlyList<AiBlock> Blocks { get; init; }

    public static AiMessage User(params AiBlock[] blocks) => new() { Role = Roles.User, Blocks = blocks };
    public static AiMessage Assistant(IReadOnlyList<AiBlock> blocks) => new() { Role = Roles.Assistant, Blocks = blocks };

    public static class Roles
    {
        public const string User = "user";
        public const string Assistant = "assistant";
    }
}

/// <summary>A tool as the provider needs to see it: a name, a description, and a JSON Schema.</summary>
public sealed record AiToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>JSON Schema for the tool's input, as a JSON object string.</summary>
    public required string InputSchemaJson { get; init; }
}

/// <summary>What the chat service asks the provider for. Carries no SDK types.</summary>
public sealed record AiGatewayRequest
{
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    /// <summary>Stable instructions. Cached provider-side, so this must not vary within a conversation.</summary>
    public required string StaticSystemPrompt { get; init; }
    /// <summary>Per-turn context (date, caller role, sites). Sits after the cache breakpoint.</summary>
    public required string DynamicSystemPrompt { get; init; }
    public required IReadOnlyList<AiMessage> Messages { get; init; }
    public required IReadOnlyList<AiToolDefinition> Tools { get; init; }
    public int MaxTokens { get; init; } = AiDefaults.MaxTokens;
}

/// <summary>What the provider answered, already mapped back into Orkyo's shape.</summary>
public sealed record AiGatewayResponse
{
    public required IReadOnlyList<AiBlock> Blocks { get; init; }
    /// <summary>Provider stop reason, verbatim: <c>end_turn</c>, <c>tool_use</c>, <c>max_tokens</c>, <c>refusal</c>, …</summary>
    public required string StopReason { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
}

/// <summary>
/// Raised when the provider call fails in a way the user should hear about. Carries a
/// stable code the UI can branch on, never the API key or the request body.
/// </summary>
public sealed class AiGatewayException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>One of: <c>credential_invalid</c>, <c>upstream_busy</c>, <c>upstream_error</c>.</summary>
    public string Code { get; } = code;
}
