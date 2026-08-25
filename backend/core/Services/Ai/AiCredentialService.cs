using Api.Constants;
using Api.Models;
using Api.Repositories;
using Api.Security.Encryption;

namespace Api.Services.Ai;

public interface IAiCredentialService
{
    /// <summary>Masked status for the admin UI. Never returns the key.</summary>
    Task<AiCredentialStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>True when a key is stored — the cheap check the chat surface needs.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>Stores or replaces the workspace's key. Throws <see cref="ArgumentException"/> on an implausible key.</summary>
    Task<AiCredentialStatus> SaveAsync(string apiKey, Guid? actorUserId, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// The decrypted key, for the chat proxy only. Never route this to a response body,
    /// a log, or an exception message.
    /// </summary>
    Task<string?> GetApiKeyAsync(CancellationToken ct = default);

    /// <summary>The model this workspace should use, falling back to the application default.</summary>
    Task<string> GetModelAsync(CancellationToken ct = default);

    Task MarkVerifiedAsync(CancellationToken ct = default);
}

/// <summary>
/// Owns the workspace's AI provider key. Encryption happens here so the repository only
/// ever sees ciphertext and the endpoint layer only ever sees a masked status.
///
/// The key is bound to the workspace through <see cref="IEncryptionService"/>'s tenant
/// associated-data, so a ciphertext restored into the wrong workspace fails to decrypt
/// rather than silently working.
/// </summary>
public sealed class AiCredentialService(
    IAiCredentialRepository repository,
    IEncryptionService encryption,
    OrgContext orgContext,
    ITenantUserService tenantUserService,
    ILogger<AiCredentialService> logger) : IAiCredentialService
{
    /// <summary>Anthropic keys carry this prefix. A shape check at the boundary catches a pasted wrong value early.</summary>
    private const string AnthropicKeyPrefix = "sk-ant-";
    private const int MinKeyLength = 20;

    public async Task<AiCredentialStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var row = await repository.GetAsync(ct);
        if (row is null) return new AiCredentialStatus { Configured = false };

        return new AiCredentialStatus
        {
            Configured = true,
            Provider = row.Provider,
            KeyHint = row.KeyHint,
            UpdatedAt = row.UpdatedAt,
            LastVerifiedAt = row.LastVerifiedAt,
        };
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        await repository.GetAsync(ct) is not null;

    public async Task<AiCredentialStatus> SaveAsync(string apiKey, Guid? actorUserId, CancellationToken ct = default)
    {
        var trimmed = (apiKey ?? "").Trim();
        if (trimmed.Length < MinKeyLength || !trimmed.StartsWith(AnthropicKeyPrefix, StringComparison.Ordinal))
            throw new ArgumentException($"An Anthropic API key starts with '{AnthropicKeyPrefix}'.", nameof(apiKey));

        var ciphertext = encryption.ProtectString(trimmed, orgContext.OrgId)
            ?? throw new InvalidOperationException("Encryption produced no value for a non-empty key.");

        await repository.UpsertAsync(ciphertext, BuildHint(trimmed), actorUserId, ct);

        await tenantUserService.RecordAuditEventAsync(
            orgContext, TenantAuditActions.AiCredentialSaved, actorUserId,
            targetType: "ai_credential", targetId: AiProviders.Anthropic, ct: ct);

        logger.LogInformation("AI credential saved for workspace {OrgId}", orgContext.OrgId);
        return await GetStatusAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid? actorUserId, CancellationToken ct = default)
    {
        var removed = await repository.DeleteAsync(ct);
        if (!removed) return false;

        await tenantUserService.RecordAuditEventAsync(
            orgContext, TenantAuditActions.AiCredentialDeleted, actorUserId,
            targetType: "ai_credential", targetId: AiProviders.Anthropic, ct: ct);

        logger.LogInformation("AI credential removed for workspace {OrgId}", orgContext.OrgId);
        return true;
    }

    public async Task<string?> GetApiKeyAsync(CancellationToken ct = default)
    {
        var row = await repository.GetAsync(ct);
        if (row is null) return null;

        try
        {
            return encryption.UnprotectString(row.ApiKeyCiphertext, orgContext.OrgId);
        }
        catch (EncryptionException ex)
        {
            // A ciphertext that will not open is a configuration fault, not a user error:
            // wrong master key, or a row restored into the wrong workspace.
            logger.LogError(ex, "Stored AI credential for workspace {OrgId} could not be decrypted", orgContext.OrgId);
            return null;
        }
    }

    public async Task<string> GetModelAsync(CancellationToken ct = default)
    {
        var row = await repository.GetAsync(ct);
        return string.IsNullOrWhiteSpace(row?.Model) ? AiDefaults.Model : row!.Model!;
    }

    public Task MarkVerifiedAsync(CancellationToken ct = default) => repository.MarkVerifiedAsync(ct);

    /// <summary>Ellipsis plus the last four characters — enough to recognize a key, useless to an attacker.</summary>
    private static string BuildHint(string apiKey) => "…" + apiKey[^4..];
}
