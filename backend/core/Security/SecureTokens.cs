using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Api.Security;

/// <summary>
/// How this codebase mints a bearer-style secret: 256 bits of CSPRNG output, base64url so
/// it survives a URL path segment untouched — and the SHA-256 digests the database stores
/// in its place.
///
/// The stored encodings are deliberately NOT unified: invitations store base64, calendar
/// feeds lowercase hex and lifecycle confirm tokens 32 hex characters, each compared with a
/// WHERE clause against rows written by earlier releases, and changing any encoding would
/// silently invalidate every live invitation, feed subscription or confirm link. Converging
/// them is a migration, not a refactor.
/// </summary>
public static class SecureTokens
{
    /// <summary>A new 256-bit token, base64url-encoded and unpadded.</summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The token's SHA-256 digest as base64 (invitations).</summary>
    public static string Sha256Base64(string token) => Convert.ToBase64String(Sha256(token));

    /// <summary>The token's SHA-256 digest as lowercase hex (calendar feeds).</summary>
    public static string Sha256Hex(string token) => Convert.ToHexStringLower(Sha256(token));

    /// <summary>
    /// The stored form of a confirm-activity token: <see cref="Sha256Hex"/> cut to 32
    /// characters (128 bits) because <c>users.lifecycle_confirm_token</c> is <c>varchar(36)</c>
    /// and applied migrations are immutable. The mailed token keeps its full 256 bits; only
    /// its digest is stored.
    /// </summary>
    public static string LifecycleConfirmTokenHash(string token) => Sha256Hex(token)[..32];

    private static byte[] Sha256(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
