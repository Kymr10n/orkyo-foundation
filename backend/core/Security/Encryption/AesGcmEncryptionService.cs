using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Api.Security.Encryption;

/// <summary>
/// AES-256-GCM implementation of <see cref="IEncryptionService"/>.
///
/// Envelope layout (both serializations encode the same logical fields):
///   string: <c>orkyoenc:v1:aesgcm256:&lt;kv&gt;:&lt;base64url(nonce)&gt;:&lt;base64url(tag||ciphertext)&gt;</c>
///   binary: <c>"ORK1"</c>(4) │ alg(1) │ keyVersion(2, BE) │ nonce(12) │ tag(16) │ ciphertext(N)
///
/// The tenant id is bound as GCM associated-data, so authentication fails if a payload is
/// decrypted under a different tenant. The 12-byte nonce is random per operation.
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const string StringPrefix = "orkyoenc:";
    private const string AlgName = "aesgcm256";
    private const byte AlgId = 1;
    private const int Version = 1;

    private static readonly byte[] BinaryMagic = "ORK1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int BinaryHeaderSize = 4 + 1 + 2; // magic + alg + keyVersion

    private readonly byte[] _masterKey;
    private readonly int _keyVersion;

    /// <param name="masterKey">32-byte AES-256 key.</param>
    /// <param name="keyVersion">Version stamped into envelopes (reserved for future per-tenant DEK rotation).</param>
    public AesGcmEncryptionService(byte[] masterKey, int keyVersion = Version)
    {
        if (masterKey is not { Length: 32 })
            throw new ArgumentException("Master encryption key must be exactly 32 bytes (AES-256).", nameof(masterKey));
        _masterKey = (byte[])masterKey.Clone();
        _keyVersion = keyVersion;
    }

    public string Algorithm => AlgName;
    public int KeyVersion => _keyVersion;

    // ── String envelope ──────────────────────────────────────────────────────

    public bool IsProtected(string? stored) =>
        stored is not null && stored.StartsWith(StringPrefix, StringComparison.Ordinal);

    public string? ProtectString(string? plaintext, Guid tenantId)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext; // null/empty carry no secret
        if (IsProtected(plaintext)) return plaintext;          // idempotent — already an envelope

        var (nonce, sealed_) = Seal(Encoding.UTF8.GetBytes(plaintext), tenantId);
        return string.Join(':',
            "orkyoenc", $"v{Version}", AlgName, _keyVersion.ToString(),
            Base64Url(nonce), Base64Url(sealed_));
    }

    public string? UnprotectString(string? stored, Guid tenantId)
    {
        if (string.IsNullOrEmpty(stored) || !IsProtected(stored)) return stored; // legacy plaintext passthrough

        var parts = stored.Split(':');
        // orkyoenc : vN : alg : kv : nonce : ct   → 6 parts
        if (parts.Length != 6 || parts[1] != $"v{Version}" || parts[2] != AlgName)
            throw new EncryptionException("Unsupported or malformed encryption envelope.");

        byte[] nonce, sealed_;
        try
        {
            nonce = FromBase64Url(parts[4]);
            sealed_ = FromBase64Url(parts[5]);
        }
        catch (FormatException ex)
        {
            throw new EncryptionException("Malformed encryption envelope.", ex);
        }
        return Encoding.UTF8.GetString(Open(nonce, sealed_, tenantId));
    }

    // ── Binary envelope ──────────────────────────────────────────────────────

    public bool IsProtected(ReadOnlySpan<byte> stored) =>
        stored.Length >= BinaryMagic.Length && stored[..BinaryMagic.Length].SequenceEqual(BinaryMagic);

    public byte[] ProtectBytes(ReadOnlySpan<byte> plaintext, Guid tenantId)
    {
        if (IsProtected(plaintext)) return plaintext.ToArray(); // idempotent

        var (nonce, sealed_) = Seal(plaintext, tenantId);
        var result = new byte[BinaryHeaderSize + NonceSize + sealed_.Length];
        var span = result.AsSpan();
        BinaryMagic.CopyTo(span);
        span[4] = AlgId;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(5, 2), (ushort)_keyVersion);
        nonce.CopyTo(span.Slice(BinaryHeaderSize, NonceSize));
        sealed_.CopyTo(span[(BinaryHeaderSize + NonceSize)..]);
        return result;
    }

    public byte[] UnprotectBytes(byte[] stored, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (!IsProtected(stored)) return stored; // legacy plaintext blob passthrough

        if (stored.Length < BinaryHeaderSize + NonceSize + TagSize)
            throw new EncryptionException("Truncated encryption envelope.");
        if (stored[4] != AlgId)
            throw new EncryptionException("Unsupported encryption algorithm.");
        // keyVersion at [5..7] reserved for future multi-key decryption.

        var nonce = stored.AsSpan(BinaryHeaderSize, NonceSize).ToArray();
        var sealed_ = stored.AsSpan(BinaryHeaderSize + NonceSize).ToArray();
        return Open(nonce, sealed_, tenantId);
    }

    // ── AES-GCM core (tag is prepended to ciphertext in the sealed buffer) ─────

    private (byte[] nonce, byte[] sealed_) Seal(ReadOnlySpan<byte> plaintext, Guid tenantId)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, tenantId.ToByteArray());

        var sealed_ = new byte[TagSize + ciphertext.Length];
        tag.CopyTo(sealed_, 0);
        ciphertext.CopyTo(sealed_, TagSize);
        return (nonce, sealed_);
    }

    private byte[] Open(byte[] nonce, byte[] sealed_, Guid tenantId)
    {
        if (nonce.Length != NonceSize || sealed_.Length < TagSize)
            throw new EncryptionException("Malformed encryption envelope.");

        var tag = sealed_.AsSpan(0, TagSize);
        var ciphertext = sealed_.AsSpan(TagSize);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(_masterKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, tenantId.ToByteArray());
        }
        catch (CryptographicException ex)
        {
            // Wrong key, wrong tenant, or tampering — never surface plaintext context.
            throw new EncryptionException("Decryption failed: authentication check did not pass.", ex);
        }
        return plaintext;
    }

    // ── base64url (no padding) ─────────────────────────────────────────────────

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
    }
}
