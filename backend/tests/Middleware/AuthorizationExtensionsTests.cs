using System.Reflection;
using System.Text.Json;
using Api.Constants;
using Api.Middleware;
using Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Middleware;

public class AuthorizationExtensionsTests
{
    [Fact]
    public async Task BuildMembershipDenial_ShouldReturnUnauthorized_WhenPrincipalMissing()
    {
        var context = CreateHttpContext(BuildServices());

        var result = InvokeBuildMembershipDenial(context);
        var payload = await ExecuteAndReadJsonAsync(result, context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        payload.GetProperty("code").GetString().Should().Be(ApiErrorCodes.SessionExpired);
        payload.GetProperty("error").GetString().Should().Be("Not authenticated");
    }

    [Fact]
    public async Task BuildMembershipDenial_ShouldReturnBreakGlassExpired_WhenPrincipalIsSiteAdmin()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "admin@orkyo.test",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = true,
        });

        var services = BuildServices(principal);

        var context = CreateHttpContext(services);

        var result = InvokeBuildMembershipDenial(context);
        var payload = await ExecuteAndReadJsonAsync(result, context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        payload.GetProperty("code").GetString().Should().Be(ApiErrorCodes.BreakGlassExpired);
        payload.GetProperty("returnTo").GetString().Should().Be("/admin");
    }

    [Fact]
    public async Task BuildMembershipDenial_ShouldReturnForbidden_WhenAuthenticatedNonSiteAdmin()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "user@orkyo.test",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = false,
        });

        var services = BuildServices(principal);

        var context = CreateHttpContext(services);

        var result = InvokeBuildMembershipDenial(context);
        var payload = await ExecuteAndReadJsonAsync(result, context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        payload.GetProperty("code").GetString().Should().Be(ApiErrorCodes.Forbidden);
        payload.TryGetProperty("returnTo", out _).Should().BeTrue();
        payload.GetProperty("returnTo").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static DefaultHttpContext CreateHttpContext(IServiceProvider services)
    {
        return new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };
    }

    private static IServiceProvider BuildServices(CurrentPrincipal? principal = null)
    {
        var services = new ServiceCollection()
            .AddLogging();

        if (principal != null)
            services.AddSingleton(principal)
                .AddSingleton<ICurrentPrincipal>(principal);

        return services.BuildServiceProvider();
    }

    private static IResult InvokeBuildMembershipDenial(HttpContext context)
    {
        var method = typeof(AuthorizationExtensions)
            .GetMethod("BuildMembershipDenial", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (IResult)method!.Invoke(null, [context])!;
    }

    private static async Task<JsonElement> ExecuteAndReadJsonAsync(IResult result, HttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;

        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return doc.RootElement.Clone();
    }
}
