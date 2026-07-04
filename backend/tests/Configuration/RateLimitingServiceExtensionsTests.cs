using System.Net;
using Api.Configuration;
using Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Orkyo.Foundation.Tests.Configuration;

/// <summary>
/// Guards the contract every edition depends on: <see cref="RateLimitingServiceExtensions.AddFoundationRateLimiting"/>
/// must register a working policy for every name in <see cref="FoundationRateLimitPolicies"/>. If a policy is
/// missing or renamed, the rate-limiter middleware throws at request time and the endpoint returns 500 — the exact
/// drift that let a foundation endpoint 500 in Community. Each policy is exercised end-to-end through the real
/// middleware, so this fails loudly rather than silently.
/// </summary>
public class RateLimitingServiceExtensionsTests
{
    public static IEnumerable<object[]> PolicyNames =>
    [
        [FoundationRateLimitPolicies.AdminOperations],
        [FoundationRateLimitPolicies.PasswordChange],
        [FoundationRateLimitPolicies.SessionBootstrap],
        [FoundationRateLimitPolicies.ContactForm],
        [FoundationRateLimitPolicies.BffAuth],
        [FoundationRateLimitPolicies.ReportingApi],
    ];

    [Theory]
    [MemberData(nameof(PolicyNames))]
    public async Task AddFoundationRateLimiting_RegistersWorkingPolicy(string policyName)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    // AddFoundationRateLimiting resolves the client IP via IClientIpAccessor at request time.
                    services.AddSingleton<IClientIpAccessor, ClientIpAccessor>();
                    services.AddFoundationRateLimiting();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapGet("/probe", () => Results.Ok()).RequireRateLimiting(policyName));
                }))
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/probe");

        // A missing/renamed policy makes the middleware throw → 500. A registered policy lets the request through.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"policy '{policyName}' must be registered by AddFoundationRateLimiting");
    }
}
