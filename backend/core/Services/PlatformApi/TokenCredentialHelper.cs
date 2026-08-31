using System.Security.Cryptography;
using System.Text;

namespace Api.Services.PlatformApi;

/// <summary>
/// The mechanics shared by every bearer-token credential class: generate a
/// <c>{scheme}_{prefix}_{base64url-secret}</c> token, HMAC-hash the secret with a pepper, parse a
/// raw token back into its parts, and compare hashes in constant time.
///
/// Deliberately knows nothing about storage, scopes or trust level — those differ per credential
/// class and are what keep the classes separate. Only the format and the crypto are shared, so a
/// change to either lands in one place instead of drifting between schemes.
/// </summary>
public static class TokenCredentialHelper
{
    private const int PrefixLength = 8;
    private const int SecretByteLength = 32;

    /// <summary>A freshly minted credential: the raw string to show once, and what to store.</summary>
    public readonly record struct GeneratedToken(string RawToken, string Prefix, string Hash);

    /// <summary>
    /// Reads the pepper that keys token hashes, preferring <paramref name="primaryValue"/> and
    /// falling back to <paramref name="fallbackValue"/>. Throws when neither is set: hashing with a
    /// known or empty pepper would make stored hashes forgeable, so this fails closed.
    /// </summary>
    public static byte[] ResolvePepper(string? primaryValue, string? fallbackValue, string context)
    {
        var pepper = !string.IsNullOrEmpty(primaryValue) ? primaryValue : fallbackValue;
        if (string.IsNullOrEmpty(pepper))
            throw new InvalidOperationException(
                $"{context}: no token pepper is configured; refusing to hash tokens with a known pepper.");
        return Encoding.UTF8.GetBytes(pepper);
    }

    public static GeneratedToken Generate(string scheme, byte[] pepper)
    {
        var prefix = GeneratePrefix();
        var secretBytes = RandomNumberGenerator.GetBytes(SecretByteLength);
        return new GeneratedToken(
            RawToken: $"{scheme}_{prefix}_{Base64UrlEncode(secretBytes)}",
            Prefix: prefix,
            Hash: ComputeHash(secretBytes, pepper));
    }

    /// <summary>
    /// Splits a raw token into its prefix (the DB lookup key) and secret bytes. Returns false for
    /// anything that is not a well-formed token of this scheme — including a token of another
    /// class, which is how each auth handler cheaply ignores credentials that are not its own.
    /// </summary>
    public static bool TryParse(string raw, string scheme, out string prefix, out byte[] secretBytes)
    {
        prefix = "";
        secretBytes = [];

        if (!raw.StartsWith(scheme + "_", StringComparison.Ordinal))
            return false;

        var rest = raw[(scheme.Length + 1)..];
        var underscoreIdx = rest.IndexOf('_');
        if (underscoreIdx < 0) return false;

        prefix = rest[..underscoreIdx];

        try
        {
            secretBytes = Base64UrlDecode(rest[(underscoreIdx + 1)..]);
            return secretBytes.Length == SecretByteLength;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Extracts just the prefix from a raw token, for logging a failure without the secret.</summary>
    public static string? ExtractPrefix(string raw, string scheme)
    {
        if (!raw.StartsWith(scheme + "_", StringComparison.Ordinal)) return null;
        var rest = raw[(scheme.Length + 1)..];
        var idx = rest.IndexOf('_');
        return idx > 0 ? rest[..idx] : null;
    }

    public static string ComputeHash(byte[] secretBytes, byte[] pepper)
    {
        using var hmac = new HMACSHA256(pepper);
        return Convert.ToHexString(hmac.ComputeHash(secretBytes)).ToLowerInvariant();
    }

    /// <summary>Constant-time hash comparison — a timing-variable compare leaks the stored hash.</summary>
    public static bool HashesMatch(string expected, string stored) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(stored));

    private static string GeneratePrefix()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> buf = stackalloc byte[PrefixLength];
        RandomNumberGenerator.Fill(buf);
        return new string(buf.ToArray().Select(b => chars[b % chars.Length]).ToArray());
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
