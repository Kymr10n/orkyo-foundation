using System.Net;
using Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Security;

public class ClientIpAccessorTests
{
    private static ClientIpAccessor New(string? trustedNetworks = null)
    {
        var dict = new Dictionary<string, string?>();
        if (trustedNetworks is not null)
            dict[ConfigKeys.SecurityTrustedProxyNetworks] = trustedNetworks;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new ClientIpAccessor(config);
    }

    private static HttpContext Ctx(string remoteIp, params (string name, string value)[] headers)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        foreach (var (name, value) in headers)
            ctx.Request.Headers[name] = value;
        return ctx;
    }

    [Fact]
    public void TrustedPrivatePeer_HonorsCfConnectingIp()
    {
        // Default trust = private/loopback peers (the nginx/Docker topology).
        var ip = New().GetClientIp(Ctx("172.19.0.15", ("CF-Connecting-IP", "203.0.113.7")));
        ip.Should().Be("203.0.113.7");
    }

    [Fact]
    public void UntrustedPublicPeer_IgnoresForgedHeader_UsesRealPeer()
    {
        // A public client hitting the backend directly cannot spoof its IP.
        var ip = New().GetClientIp(Ctx("198.51.100.9", ("CF-Connecting-IP", "10.0.0.1")));
        ip.Should().Be("198.51.100.9");
    }

    [Fact]
    public void TrustedPeer_NoCfHeader_UsesLeftmostNonProxyXff()
    {
        var ip = New().GetClientIp(Ctx("10.0.0.5",
            ("X-Forwarded-For", "203.0.113.7, 10.0.0.5, 172.19.0.1")));
        ip.Should().Be("203.0.113.7");
    }

    [Fact]
    public void TrustedPeer_MalformedHeaders_FallsBackToPeer()
    {
        var ip = New().GetClientIp(Ctx("127.0.0.1",
            ("CF-Connecting-IP", "not-an-ip"),
            ("X-Forwarded-For", "garbage, also-bad")));
        ip.Should().Be("127.0.0.1");
    }

    [Fact]
    public void NoForwardedHeaders_ReturnsPeerIp()
    {
        New().GetClientIp(Ctx("172.19.0.15")).Should().Be("172.19.0.15");
    }

    [Fact]
    public void IPv4MappedPrivatePeer_IsTrusted_HonorsForwardedHeader()
    {
        // Kestrel can surface an IPv4 peer as ::ffff:10.0.0.5 — still a private proxy.
        var ip = New().GetClientIp(Ctx("::ffff:10.0.0.5", ("CF-Connecting-IP", "203.0.113.7")));
        ip.Should().Be("203.0.113.7");
    }

    [Fact]
    public void IPv4MappedPeer_NoForwarded_NormalizedToDottedQuad()
    {
        // ::ffff:172.18.0.7 must display as 172.18.0.7, not the mapped form.
        New().GetClientIp(Ctx("::ffff:172.18.0.7")).Should().Be("172.18.0.7");
    }

    [Fact]
    public void IPv4MappedForwardedHeader_NormalizedToDottedQuad()
    {
        New().GetClientIp(Ctx("10.0.0.5", ("CF-Connecting-IP", "::ffff:203.0.113.7")))
            .Should().Be("203.0.113.7");
    }

    [Fact]
    public void ConfiguredCidr_TrustsOnlyListedNetwork()
    {
        var accessor = New("172.19.0.0/24");
        // In-range proxy → header honored.
        accessor.GetClientIp(Ctx("172.19.0.20", ("CF-Connecting-IP", "203.0.113.7")))
            .Should().Be("203.0.113.7");
        // A private peer NOT in the configured range is no longer trusted.
        accessor.GetClientIp(Ctx("10.0.0.5", ("CF-Connecting-IP", "203.0.113.7")))
            .Should().Be("10.0.0.5");
    }

    [Fact]
    public void InvalidConfiguredCidr_ThrowsOnConstruction()
    {
        var act = () => New("not-a-cidr");
        act.Should().Throw<InvalidOperationException>();
    }
}
