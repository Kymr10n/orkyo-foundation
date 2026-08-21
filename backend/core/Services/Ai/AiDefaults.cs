namespace Api.Services.Ai;

/// <summary>
/// Fixed knobs for the assistant. These are code constants rather than tenant settings
/// on purpose: a workspace admin configures <em>access and budget</em>, not model
/// mechanics. The credential row carries a nullable <c>model</c> column as an escape
/// hatch, because migrations are immutable and adding the column later would cost one.
/// </summary>
public static class AiDefaults
{
    /// <summary>
    /// Re-audit <see cref="AiSystemPrompt"/> whenever this changes — prompt guidance is
    /// tuned per model generation, and a model swap can silently invalidate it.
    /// </summary>
    public const string Model = "claude-opus-5";

    /// <summary>
    /// Ceiling on one turn's output. Thinking counts against this on Claude Opus 5, so it
    /// has to leave room for reasoning as well as the reply.
    /// </summary>
    public const int MaxTokens = 16000;

    /// <summary>
    /// How many times the loop may run tools before it must produce prose. Bounds a
    /// pathological loop without truncating realistic multi-step answers.
    /// </summary>
    public const int MaxToolIterations = 6;

    /// <summary>Wall-clock ceiling for one turn, independent of client disconnects.</summary>
    public static readonly TimeSpan TurnDeadline = TimeSpan.FromMinutes(5);

    /// <summary>Transcript guards. Beyond these the client is told to start a new conversation.</summary>
    public const int MaxTranscriptMessages = 40;
    public const int MaxTranscriptBytes = 256 * 1024;
}
