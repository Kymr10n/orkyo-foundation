using System.Security.Cryptography;

namespace Api.Security;

/// <summary>
/// How this codebase mints a bearer-style secret: 256 bits of CSPRNG output, base64url so
/// it survives a URL path segment untouched.
///
/// Only the generation is shared. The hashes that go in the database are deliberately NOT
/// unified: invitations store base64 and calendar feeds store lowercase hex, both are
/// compared with a WHERE clause against rows written by earlier releases, and changing
/// either encoding would silently invalidate every live invitation or feed subscription.
/// Converging them is a migration, not a refactor.
/// </summary>
public static class SecureTokens
{
    /// <summary>A new 256-bit token, base64url-encoded and unpadded.</summary>
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
