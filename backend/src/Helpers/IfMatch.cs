using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Api.Helpers;

/// <summary>
/// Reads the row version a write expects from its <c>If-Match</c> header. The version is the
/// <c>version</c> field of the DTO the client read, sent as one quoted number (<c>"123"</c>).
/// </summary>
public static class IfMatch
{
    /// <summary>
    /// The expected version, or null when the header is absent or <c>*</c> (no check). A weak
    /// tag, a list, or any other value is a client error: RFC 9110 compares If-Match strongly.
    /// </summary>
    public static uint? Parse(HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString().Trim();
        if (raw.Length == 0 || raw == "*") return null;

        if (raw.Length > 2 && raw[0] == '"' && raw[^1] == '"'
            && uint.TryParse(raw.AsSpan(1, raw.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            return version;

        throw new ArgumentException("If-Match must be one quoted version number, for example \"123\".");
    }
}
