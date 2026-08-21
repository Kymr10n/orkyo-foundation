using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

/// <summary>One stored provider credential, ciphertext included. Server-side only — never a wire type.</summary>
public sealed record AiCredentialRow
{
    public string Provider { get; init; } = AiProviders.Anthropic;
    public string ApiKeyCiphertext { get; init; } = "";
    public string KeyHint { get; init; } = "";
    public string? Model { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? LastVerifiedAt { get; init; }
}

public interface IAiCredentialRepository
{
    Task<AiCredentialRow?> GetAsync(CancellationToken ct = default);
    Task UpsertAsync(string ciphertext, string keyHint, Guid? actorUserId, CancellationToken ct = default);
    Task<bool> DeleteAsync(CancellationToken ct = default);
    Task MarkVerifiedAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads and writes the single AI credential row in the workspace's own database.
/// Stores ciphertext verbatim — encryption is the service's job, so the repository
/// never handles a plaintext key.
/// </summary>
public sealed class AiCredentialRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IAiCredentialRepository
{
    public async Task<AiCredentialRow?> GetAsync(CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QuerySingleOrDefaultAsync(@"
            SELECT provider, api_key_ciphertext, key_hint, model, updated_at, last_verified_at
            FROM ai_credentials
            WHERE provider = @provider",
            p => p.AddWithValue("provider", AiProviders.Anthropic),
            r => new AiCredentialRow
            {
                Provider = r.GetString(0),
                ApiKeyCiphertext = r.GetString(1),
                KeyHint = r.GetString(2),
                Model = r.IsDBNull(3) ? null : r.GetString(3),
                UpdatedAt = r.GetDateTime(4),
                LastVerifiedAt = r.IsDBNull(5) ? null : r.GetDateTime(5),
            }, ct);
    }

    public async Task UpsertAsync(string ciphertext, string keyHint, Guid? actorUserId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.ExecuteAsync(@"
            INSERT INTO ai_credentials (provider, api_key_ciphertext, key_hint, created_by_user_id, updated_at)
            VALUES (@provider, @ciphertext, @hint, @actor, NOW())
            ON CONFLICT (provider) DO UPDATE SET
                api_key_ciphertext = @ciphertext,
                key_hint           = @hint,
                updated_at         = NOW(),
                -- A replaced key is unverified until it is probed again.
                last_verified_at   = NULL",
            p =>
            {
                p.AddWithValue("provider", AiProviders.Anthropic);
                p.AddWithValue("ciphertext", ciphertext);
                p.AddWithValue("hint", keyHint);
                p.AddWithValue("actor", actorUserId.HasValue ? actorUserId.Value : DBNull.Value);
            }, ct);
    }

    public async Task<bool> DeleteAsync(CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        var rows = await conn.ExecuteAsync(
            "DELETE FROM ai_credentials WHERE provider = @provider",
            p => p.AddWithValue("provider", AiProviders.Anthropic), ct);
        return rows > 0;
    }

    public async Task MarkVerifiedAsync(CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.ExecuteAsync(
            "UPDATE ai_credentials SET last_verified_at = NOW() WHERE provider = @provider",
            p => p.AddWithValue("provider", AiProviders.Anthropic), ct);
    }
}
