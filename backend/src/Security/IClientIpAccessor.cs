using Microsoft.AspNetCore.Http;

namespace Api.Security;

/// <summary>
/// Resolves the real client IP for a request behind the reverse-proxy chain
/// (Cloudflare → nginx → backend). Reads forwarded-IP headers only when the
/// immediate peer is a trusted proxy, so a public client cannot spoof its IP.
/// </summary>
public interface IClientIpAccessor
{
    /// <summary>
    /// The best-effort real client IP, or the direct peer IP when no trusted
    /// forwarded header is present. Never throws; returns null only if no IP at all.
    /// </summary>
    string? GetClientIp(HttpContext ctx);
}
