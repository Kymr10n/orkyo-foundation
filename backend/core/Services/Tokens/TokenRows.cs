using Api.Helpers;
using Npgsql;

namespace Api.Services.Tokens;

/// <summary>
/// The shape shared by every tenant API bearer token, and the one place that reads one from a row.
/// </summary>
/// <remarks>
/// <para>
/// Two tables hold tokens of this kind — <c>api_access_tokens</c> for the platform API and
/// <c>reporting_api_tokens</c> for the reporting API. They are separate features on separate
/// migrations, but a token is one concept: a prefix, a hash, a scope string, and the three
/// timestamps that decide whether it still works. Before this type the concept was written out
/// twice, including two byte-identical mapper classes whose only difference was the namespace.
/// </para>
/// <para>
/// Divergence is still cheap. A column that belongs to only one table becomes a property on that
/// table's derived record, not here; only what both tables genuinely share lives on the base.
/// <see cref="TokenRowMapper.SummaryColumns"/> and <see cref="TokenRowMapper.RecordColumns"/>
/// name the columns once, so a SELECT list and its reader cannot drift apart.
/// </para>
/// </remarks>
public abstract record TokenSummaryBase
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Name { get; init; } = "";
    public string TokenPrefix { get; init; } = "";
    public string Scopes { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public DateTime? LastUsedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; init; }

    /// <summary>
    /// Evaluated against the caller's clock at read time and stored, rather than recomputed on
    /// access, so a listing is internally consistent: every row in one page is judged against
    /// the same instant.
    /// </summary>
    public bool IsActive { get; init; }
}

/// <summary>
/// A token row including its hash — the shape the verification path needs.
/// </summary>
/// <remarks>
/// <see cref="IsActive"/> is computed here rather than stored, because a record is read to decide
/// one request and the answer must reflect the clock now, not when the row was fetched.
/// </remarks>
public abstract record TokenRecordBase
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Name { get; init; } = "";
    public string TokenPrefix { get; init; } = "";
    public string TokenHash { get; init; } = "";
    public string Scopes { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public DateTime? LastUsedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; init; }

    public bool IsActive =>
        RevokedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > DateTime.UtcNow);
}

/// <summary>
/// Reads a token row by column name into the caller's record type.
/// </summary>
/// <remarks>
/// Generic over the concrete record so each table keeps its own public DTO and its own defaults,
/// while the column names and the reads live here once. Every read is by name: a positional read
/// is silently wrong the moment a SELECT list is reordered.
/// </remarks>
internal static class TokenRowMapper
{
    internal const string SummaryColumns =
        "id, tenant_id, name, token_prefix, scopes, created_at, created_by_user_id, "
        + "last_used_at, expires_at, revoked_at";

    internal const string RecordColumns =
        "id, tenant_id, name, token_prefix, token_hash, scopes, created_at, created_by_user_id, "
        + "last_used_at, expires_at, revoked_at";

    internal static TSummary MapSummary<TSummary>(NpgsqlDataReader reader, DateTime nowUtc)
        where TSummary : TokenSummaryBase, new()
    {
        var revokedAt = reader.GetNullableDateTime("revoked_at");
        var expiresAt = reader.GetNullableDateTime("expires_at");
        return new TSummary
        {
            Id = reader.GetGuid("id"),
            TenantId = reader.GetGuid("tenant_id"),
            Name = reader.GetString("name"),
            TokenPrefix = reader.GetString("token_prefix"),
            Scopes = reader.GetString("scopes"),
            CreatedAtUtc = reader.GetDateTime("created_at"),
            CreatedByUserId = reader.GetNullableGuid("created_by_user_id"),
            LastUsedAtUtc = reader.GetNullableDateTime("last_used_at"),
            ExpiresAtUtc = expiresAt,
            RevokedAtUtc = revokedAt,
            IsActive = revokedAt is null && (expiresAt is null || expiresAt > nowUtc),
        };
    }

    internal static TRecord MapRecord<TRecord>(NpgsqlDataReader reader)
        where TRecord : TokenRecordBase, new() => new()
        {
            Id = reader.GetGuid("id"),
            TenantId = reader.GetGuid("tenant_id"),
            Name = reader.GetString("name"),
            TokenPrefix = reader.GetString("token_prefix"),
            TokenHash = reader.GetString("token_hash"),
            Scopes = reader.GetString("scopes"),
            CreatedAtUtc = reader.GetDateTime("created_at"),
            CreatedByUserId = reader.GetNullableGuid("created_by_user_id"),
            LastUsedAtUtc = reader.GetNullableDateTime("last_used_at"),
            ExpiresAtUtc = reader.GetNullableDateTime("expires_at"),
            RevokedAtUtc = reader.GetNullableDateTime("revoked_at"),
        };
}
