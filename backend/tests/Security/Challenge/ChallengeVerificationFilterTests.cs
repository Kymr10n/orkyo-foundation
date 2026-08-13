using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Security;
using Api.Security.Challenge;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Security.Challenge;

/// <summary>
/// The filter is the only thing standing between a public anonymous form and the
/// handler behind it, so its reject path is covered here rather than left to the
/// endpoint tests: those assert the endpoint's own contract and would still pass if
/// the filter let every request through.
/// </summary>
public class ChallengeVerificationFilterTests
{
    private sealed record ProtectedRequest(string? ChallengeToken) : IChallengeProtectedRequest;

    private sealed class StubProvider(ChallengeVerificationResult result) : IChallengeProvider
    {
        public string? LastToken { get; private set; }
        public string? LastClientIp { get; private set; }

        public Task<ChallengeVerificationResult> VerifyAsync(string token, string clientIp, CancellationToken ct = default)
        {
            LastToken = token;
            LastClientIp = clientIp;
            return Task.FromResult(result);
        }
    }

    private sealed class StubClientIpAccessor(string? ip) : IClientIpAccessor
    {
        public string? GetClientIp(HttpContext context) => ip;
    }

    private static (EndpointFilterInvocationContext Context, StubProvider Provider) BuildContext(
        ChallengeVerificationResult result,
        string? clientIp = "203.0.113.7",
        params object[] arguments)
    {
        var provider = new StubProvider(result);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IChallengeProvider>(provider)
            .AddSingleton<IClientIpAccessor>(new StubClientIpAccessor(clientIp))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };
        httpContext.Request.Path = "/api/contact";

        return (EndpointFilterInvocationContext.Create(httpContext, arguments), provider);
    }

    private static async Task<(int Status, JsonElement Payload)> ExecuteAsync(object? result)
    {
        var typed = result.Should().BeAssignableTo<IResult>().Subject;
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };

        await typed.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, json.RootElement.Clone());
    }

    // ── reject path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ChallengeFails_Returns403AndDoesNotCallNext()
    {
        var (context, _) = BuildContext(
            new ChallengeVerificationResult(false, "invalid-input-response"),
            arguments: new object[] { new ProtectedRequest("bad-token") });
        var nextCalled = false;

        var filter = new ChallengeVerificationFilter();
        var result = await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextCalled.Should().BeFalse("a failed challenge must never reach the handler");
        var (status, payload) = await ExecuteAsync(result);
        status.Should().Be(StatusCodes.Status403Forbidden);
        payload.GetProperty("code").GetString().Should().Be(ErrorCodes.ChallengeFailed);
    }

    // ── pass-through path ─────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ChallengeSucceeds_CallsNext()
    {
        var (context, _) = BuildContext(
            new ChallengeVerificationResult(true),
            arguments: new object[] { new ProtectedRequest("good-token") });
        var nextCalled = false;

        var filter = new ChallengeVerificationFilter();
        await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        nextCalled.Should().BeTrue();
    }

    // ── argument handling ─────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NoProtectedRequestArgument_VerifiesAnEmptyToken()
    {
        // A handler with no IChallengeProtectedRequest argument must still be verified,
        // not silently waved through: the provider decides what an empty token means.
        var (context, provider) = BuildContext(
            new ChallengeVerificationResult(true),
            arguments: new object[] { "not-a-protected-request" });

        var filter = new ChallengeVerificationFilter();
        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        provider.LastToken.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_NullChallengeToken_VerifiesAnEmptyToken()
    {
        var (context, provider) = BuildContext(
            new ChallengeVerificationResult(true),
            arguments: new object[] { new ProtectedRequest(null) });

        var filter = new ChallengeVerificationFilter();
        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        provider.LastToken.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_PassesTheResolvedClientIpToTheProvider()
    {
        var (context, provider) = BuildContext(
            new ChallengeVerificationResult(true),
            clientIp: "198.51.100.42",
            arguments: new object[] { new ProtectedRequest("t") });

        var filter = new ChallengeVerificationFilter();
        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        provider.LastClientIp.Should().Be("198.51.100.42");
    }

    [Fact]
    public async Task InvokeAsync_UnresolvableClientIp_VerifiesWithAnEmptyIp()
    {
        // Siteverify treats remoteip as optional, so an unknown IP must not throw.
        var (context, provider) = BuildContext(
            new ChallengeVerificationResult(true),
            clientIp: null,
            arguments: new object[] { new ProtectedRequest("t") });

        var filter = new ChallengeVerificationFilter();
        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        provider.LastClientIp.Should().BeEmpty();
    }
}
