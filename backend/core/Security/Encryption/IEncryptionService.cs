namespace Api.Security.Encryption;

/// <summary>
/// Application-level authenticated encryption for sensitive data at rest.
///
/// Two serializations share one AES-256-GCM core:
///   • text fields  → a self-describing string envelope
///     (<c>orkyoenc:v1:aesgcm256:&lt;keyVersion&gt;:&lt;b64nonce&gt;:&lt;b64ct&gt;</c>)
///   • binary blobs → a compact binary envelope (magic + header + nonce + tag + ciphertext)
///
/// Ciphertext is cryptographically bound to its <paramref name="tenantId"/> via the GCM
/// associated-data, so a blob/field moved to another tenant fails authentication.
///
/// Reads are migration-safe: <see cref="UnprotectString"/> / <see cref="UnprotectBytes"/>
/// pass through values that are not Orkyo envelopes (legacy plaintext), so code can read
/// old and new values during a backfill. Writes are idempotent: protecting an
/// already-protected value returns it unchanged.
///
/// The API takes <c>tenantId</c> and is versioned so per-tenant data-encryption keys can be
/// introduced later without changing callers (currently a single master key, version 1).
/// </summary>
public interface IEncryptionService
{
    /// <summary>Encrypts a text value into a string envelope. Null/empty pass through unchanged; already-protected values are returned as-is.</summary>
    string? ProtectString(string? plaintext, Guid tenantId);

    /// <summary>Decrypts a string envelope. Values that are not Orkyo envelopes (legacy plaintext) pass through unchanged.</summary>
    string? UnprotectString(string? stored, Guid tenantId);

    /// <summary>Encrypts bytes into a binary envelope.</summary>
    byte[] ProtectBytes(ReadOnlySpan<byte> plaintext, Guid tenantId);

    /// <summary>Decrypts a binary envelope. Data that is not an Orkyo envelope (legacy plaintext blob) is returned unchanged.</summary>
    byte[] UnprotectBytes(byte[] stored, Guid tenantId);

    /// <summary>True if the value is an Orkyo string envelope.</summary>
    bool IsProtected(string? stored);

    /// <summary>True if the value is an Orkyo binary envelope.</summary>
    bool IsProtected(ReadOnlySpan<byte> stored);

    /// <summary>Algorithm identifier stamped into envelopes written now (e.g. "aesgcm256"). For metadata columns.</summary>
    string Algorithm { get; }

    /// <summary>Key version stamped into envelopes written now. For metadata columns.</summary>
    int KeyVersion { get; }
}

/// <summary>Thrown when a payload cannot be decrypted (wrong key, wrong tenant, tampering, or an unknown envelope version/algorithm). Never carries plaintext.</summary>
public sealed class EncryptionException : Exception
{
    public EncryptionException(string message) : base(message) { }
    public EncryptionException(string message, Exception inner) : base(message, inner) { }
}
