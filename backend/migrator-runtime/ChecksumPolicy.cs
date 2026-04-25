using System.Security.Cryptography;
using System.Text;

namespace Orkyo.Migrator;

/// <summary>
/// Single source of truth for migration-script checksums. Hashes the SQL after
/// normalizing line endings (CRLF / CR → LF) so that platform-specific line-ending
/// drift in source control doesn't invalidate stored checksums. Output is a lowercase
/// 64-char SHA-256 hex string.
/// </summary>
public static class ChecksumPolicy
{
    public const string Algorithm = "SHA256";

    public static string Compute(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var normalized = Normalize(sql);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    internal static string Normalize(string sql) =>
        sql.Replace("\r\n", "\n").Replace("\r", "\n");
}
