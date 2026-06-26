using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Api.Security;

/// <summary>
/// Resolves the real client IP behind the Cloudflare → nginx → backend chain.
///
/// Security model: forwarded-IP headers are only believed when the *immediate*
/// peer (Connection.RemoteIpAddress) is a trusted proxy. By default "trusted"
/// means a private (RFC1918 / ULA) or loopback address — which is exactly the
/// Docker/nginx topology here (the backend is never reachable directly from the
/// public internet, only via nginx on the internal network). Operators can
/// tighten this with the optional <see cref="ConfigKeys.SecurityTrustedProxyNetworks"/>
/// CIDR allow-list. A public client that hits the backend directly (misconfig)
/// can never spoof its IP — its forged headers are ignored and the real peer IP
/// is used instead (fail-safe, never worse than today's behaviour).
/// </summary>
public sealed class ClientIpAccessor : IClientIpAccessor
{
    // Set by the trusted edge (nginx from Cloudflare's connection); a client cannot
    // forge this end-to-end because nginx overwrites it.
    private const string CfConnectingIpHeader = "CF-Connecting-IP";
    private const string XForwardedForHeader = "X-Forwarded-For";

    private readonly IReadOnlyList<(IPAddress Network, int Prefix)>? _trustedNetworks;

    public ClientIpAccessor(IConfiguration configuration)
    {
        var configured = configuration[ConfigKeys.SecurityTrustedProxyNetworks];
        _trustedNetworks = string.IsNullOrWhiteSpace(configured) ? null : ParseCidrs(configured);
    }

    public string? GetClientIp(HttpContext ctx)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        var remoteString = remote is null ? null : Format(remote);

        // Only trust forwarded headers when the direct peer is a trusted proxy.
        if (remote is null || !IsTrustedProxy(remote))
            return remoteString;

        // Cloudflare's connecting IP (set by nginx) is the most reliable.
        var cf = ctx.Request.Headers[CfConnectingIpHeader].FirstOrDefault();
        if (TryNormalize(cf, out var cfIp))
            return cfIp;

        // Otherwise the left-most X-Forwarded-For hop that is not itself a proxy.
        var xff = ctx.Request.Headers[XForwardedForHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(xff))
        {
            foreach (var hop in xff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryNormalize(hop, out var hopIp) && IPAddress.TryParse(hopIp, out var parsed) && !IsTrustedProxy(parsed))
                    return hopIp;
            }
        }

        return remoteString;
    }

    private bool IsTrustedProxy(IPAddress ip)
    {
        if (_trustedNetworks is not null)
            return _trustedNetworks.Any(n => InNetwork(ip, n.Network, n.Prefix));

        return IsPrivateOrLoopback(ip);
    }

    private static bool TryNormalize(string? candidate, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var trimmed = candidate.Trim();
        if (!IPAddress.TryParse(trimmed, out var parsed))
            return false;

        normalized = Format(parsed);
        // Column is varchar(45); IPv4/IPv6 textual forms always fit, but guard anyway.
        return normalized.Length <= 45;
    }

    /// <summary>
    /// Canonical display form: unwrap IPv4-mapped IPv6 (::ffff:a.b.c.d) to the
    /// dotted-quad a.b.c.d; genuine IPv6 keeps .NET's canonical compressed form.
    /// </summary>
    private static string Format(IPAddress ip)
        => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        // Kestrel may surface IPv4 peers as IPv4-mapped IPv6 (::ffff:10.0.0.1);
        // unwrap so the RFC1918 checks below see the real IPv4.
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10                                   // 10.0.0.0/8
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254);                // 169.254.0.0/16 link-local
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;
            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC;                       // fc00::/7 unique-local
        }

        return false;
    }

    private static List<(IPAddress, int)> ParseCidrs(string csv)
    {
        var result = new List<(IPAddress, int)>();
        foreach (var entry in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('/', 2);
            if (!IPAddress.TryParse(parts[0], out var network))
                throw new InvalidOperationException(
                    $"{ConfigKeys.SecurityTrustedProxyNetworks}: invalid network '{entry}'");

            var bits = network.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
            var prefix = bits;
            if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > bits))
                throw new InvalidOperationException(
                    $"{ConfigKeys.SecurityTrustedProxyNetworks}: invalid prefix in '{entry}'");

            result.Add((network, prefix));
        }
        return result;
    }

    private static bool InNetwork(IPAddress ip, IPAddress network, int prefix)
    {
        if (ip.AddressFamily != network.AddressFamily)
            return false;

        var ipBytes = ip.GetAddressBytes();
        var netBytes = network.GetAddressBytes();
        var fullBytes = prefix / 8;
        var remBits = prefix % 8;

        for (var i = 0; i < fullBytes; i++)
            if (ipBytes[i] != netBytes[i])
                return false;

        if (remBits == 0)
            return true;

        var mask = (byte)(0xFF << (8 - remBits));
        return (ipBytes[fullBytes] & mask) == (netBytes[fullBytes] & mask);
    }
}
