using System.Text;
using Api.Helpers;
using Api.Repositories;
using Api.Security;

namespace Api.Services.Ai;

public interface IAiConversationService
{
    Task<IReadOnlyList<AiConversationSummary>> ListAsync(CancellationToken ct = default);
    Task<AiConversationRow?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(Guid id, string title, string entriesJson, string transcriptJson, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Saved conversations for the person in this request.
///
/// The caller never names an owner: it is always <see cref="ICurrentPrincipal.UserId"/>.
/// That is what keeps one member out of another's notes — there is no parameter to get
/// wrong, and no code path where an id from the client selects whose row is touched.
/// </summary>
public sealed class AiConversationService(
    IAiConversationRepository repository,
    ICurrentPrincipal principal) : IAiConversationService
{
    /// <summary>
    /// How many conversations one person keeps. Older ones are deleted on write, so the
    /// cap is enforced by the act that creates the pressure — there is no cleanup job to
    /// schedule, and none to fail silently.
    /// </summary>
    public const int KeepPerUser = 20;

    /// <summary>Long enough for a sentence of context, short enough to sit in a menu.</summary>
    public const int MaxTitleLength = 120;

    public Task<IReadOnlyList<AiConversationSummary>> ListAsync(CancellationToken ct = default) =>
        repository.ListAsync(principal.UserId, ct);

    public Task<AiConversationRow?> GetAsync(Guid id, CancellationToken ct = default) =>
        repository.GetAsync(principal.UserId, id, ct);

    public async Task SaveAsync(Guid id, string title, string entriesJson, string transcriptJson,
        CancellationToken ct = default)
    {
        // The same ceilings the chat turn enforces. A conversation that could never be
        // sent is not worth storing, and this is the boundary where an oversized one
        // would otherwise become permanent.
        // ArgumentException is this codebase's boundary-validation signal: the handler
        // maps it to a 400 and the message is the user-facing contract.
        if (Encoding.UTF8.GetByteCount(transcriptJson) > AiDefaults.MaxTranscriptBytes
            || Encoding.UTF8.GetByteCount(entriesJson) > AiDefaults.MaxTranscriptBytes)
        {
            throw new ArgumentException(
                "This conversation is too large to save. Start a new one to continue.");
        }

        var trimmed = title.Trim();
        if (trimmed.Length == 0) trimmed = "Conversation";
        if (trimmed.Length > MaxTitleLength) trimmed = trimmed[..MaxTitleLength];

        var written = await repository.UpsertAsync(principal.UserId, id, trimmed, entriesJson, transcriptJson, ct);
        if (!written)
        {
            // The id exists and belongs to someone else. Reporting success here would tell
            // the client its conversation is saved when the next read returns nothing.
            throw new NotFoundException("Conversation", id);
        }

        await repository.TrimAsync(principal.UserId, KeepPerUser, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        repository.DeleteAsync(principal.UserId, id, ct);
}
