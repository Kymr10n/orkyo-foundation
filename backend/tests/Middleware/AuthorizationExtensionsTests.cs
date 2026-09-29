using System.Net;
using System.Reflection;
using System.Text.Json;
using Api.Constants;
using Api.Middleware;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Orkyo.Shared;

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
        payload.GetProperty("detail").GetString().Should().Be("Not authenticated");
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
        payload.GetProperty("returnTo").GetString().Should().Be("/site-admin");
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

    // ── Endpoint filter helper ─────────────────────────────────────────────

    private static async Task<int> InvokeFilteredEndpointAsync(
        Action<RouteHandlerBuilder> applyFilter,
        ICurrentPrincipal? principal = null,
        IAuthorizationContext? authContext = null,
        ICurrentTenant? currentTenant = null)
    {
        var response = await InvokeFilteredEndpointResponseAsync(
            applyFilter, principal, authContext, currentTenant);
        return (int)response.StatusCode;
    }

    private static Task<HttpResponseMessage> InvokeFilteredEndpointResponseAsync(
        Action<RouteHandlerBuilder> applyFilter,
        ICurrentPrincipal? principal = null,
        IAuthorizationContext? authContext = null,
        ICurrentTenant? currentTenant = null)
        => SendAsync(app => applyFilter(app.MapGet("/test", () => Results.Ok())), HttpMethod.Get,
            principal, authContext, currentTenant);

    /// <summary>One route under a group carrying <paramref name="convention"/>, called with <paramref name="method"/>.</summary>
    private static async Task<int> InvokeGroupEndpointAsync(
        Func<RouteGroupBuilder, RouteGroupBuilder> convention,
        HttpMethod method,
        ICurrentPrincipal? principal = null,
        IAuthorizationContext? authContext = null,
        ICurrentTenant? currentTenant = null)
    {
        var response = await InvokeGroupEndpointResponseAsync(convention, method, principal, authContext, currentTenant);
        return (int)response.StatusCode;
    }

    private static Task<HttpResponseMessage> InvokeGroupEndpointResponseAsync(
        Func<RouteGroupBuilder, RouteGroupBuilder> convention,
        HttpMethod method,
        ICurrentPrincipal? principal = null,
        IAuthorizationContext? authContext = null,
        ICurrentTenant? currentTenant = null)
        => SendAsync(
            app => convention(app.MapGroup("/")).MapMethods("/test", [method.Method], () => Results.Ok()),
            method, principal, authContext, currentTenant);

    private static async Task<HttpResponseMessage> SendAsync(
        Action<WebApplication> map,
        HttpMethod method,
        ICurrentPrincipal? principal,
        IAuthorizationContext? authContext,
        ICurrentTenant? currentTenant)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test });
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();

        if (principal != null)
            builder.Services.AddSingleton<ICurrentPrincipal>(principal);
        if (authContext != null)
            builder.Services.AddSingleton<IAuthorizationContext>(authContext);
        if (currentTenant != null)
            builder.Services.AddSingleton<ICurrentTenant>(currentTenant);

        var app = builder.Build();
        map(app);

        await app.StartAsync();
        try
        {
            return await app.GetTestClient().SendAsync(new HttpRequestMessage(method, "/test"));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static CurrentTenant TenantScope(string slug = "acme")
    {
        var tenant = new CurrentTenant();
        tenant.SetContext(new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = slug,
            TenantDbConnectionString = "Host=localhost;Database=test",
            Status = "active",
        });
        return tenant;
    }

    // ── RequireSiteAdmin ───────────────────────────────────────────────────

    [Fact]
    public async Task RequireSiteAdmin_WhenNoPrincipal_Returns401()
    {
        var status = await InvokeFilteredEndpointAsync(route => route.RequireSiteAdmin());

        status.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequireSiteAdmin_WhenNotAuthenticated_Returns401()
    {
        var principal = new CurrentPrincipal(); // not authenticated

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireSiteAdmin(),
            principal: principal);

        status.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequireSiteAdmin_WhenAuthenticatedNotSiteAdmin_Returns403()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "user@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = false,
        });

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireSiteAdmin(),
            principal: principal);

        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequireSiteAdmin_WhenSiteAdmin_CallsNext()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = true,
        });

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireSiteAdmin(),
            principal: principal);

        status.Should().Be((int)HttpStatusCode.OK);
    }

    // ── RequireMemberReadEditorWrite: a write needs Editor ────────────────

    [Fact]
    public async Task EditorWrite_WhenNoAuthContext_Returns401()
    {
        // No principal, no auth context → BuildMembershipDenial → unauthenticated → 401
        var status = await InvokeGroupEndpointAsync(
            group => group.RequireMemberReadEditorWrite(), HttpMethod.Post);

        status.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EditorWrite_WhenNotMember_Returns403()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "user@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = false,
        });
        var authCtx = new CurrentAuthorizationContext(); // no context set → IsMember = false

        var status = await InvokeGroupEndpointAsync(
            group => group.RequireMemberReadEditorWrite(), HttpMethod.Post,
            principal: principal,
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EditorWrite_WhenRoleNotAllowed_Returns403()
    {
        var authCtx = new CurrentAuthorizationContext();
        authCtx.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Viewer,
        });

        var status = await InvokeGroupEndpointAsync(
            group => group.RequireMemberReadEditorWrite(), HttpMethod.Post,
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EditorWrite_WhenRoleAllowed_CallsNext()
    {
        var authCtx = new CurrentAuthorizationContext();
        authCtx.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Editor,
        });

        var status = await InvokeGroupEndpointAsync(
            group => group.RequireMemberReadEditorWrite(), HttpMethod.Post,
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.OK);
    }

    // ── RequireTenantMembership ────────────────────────────────────────────

    [Fact]
    public async Task RequireTenantMembership_WhenNoAuthContext_Returns401()
    {
        // No principal, no auth context → BuildMembershipDenial → unauthenticated → 401
        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireTenantMembership());

        status.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequireTenantMembership_WhenNotMember_Returns403()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "user@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = false,
        });
        var authCtx = new CurrentAuthorizationContext(); // no context set → IsMember = false

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireTenantMembership(),
            principal: principal,
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequireTenantMembership_WhenMember_CallsNext()
    {
        var authCtx = new CurrentAuthorizationContext();
        authCtx.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Viewer,
        });

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireTenantMembership(),
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.OK);
    }

    // ── RequireAdminArea ───────────────────────────────────────────────────

    [Fact]
    public async Task AdminArea_WhenNoPrincipal_Returns401()
    {
        var status = await InvokeGroupEndpointAsync(group => group.RequireAdminArea(), HttpMethod.Get);

        status.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminArea_WhenAuthenticatedWithNoAdminAccess_Returns403()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "user@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = false,
        });
        var authCtx = new CurrentAuthorizationContext(); // no context → not tenant admin

        var status = await InvokeGroupEndpointAsync(
            group => group.RequireAdminArea(), HttpMethod.Get,
            principal: principal,
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminArea_WhenSiteAdmin_CallsNext()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = true,
        });

        var status = await InvokeGroupEndpointAsync(
            group => group.RequireAdminArea(), HttpMethod.Get,
            principal: principal);

        status.Should().Be((int)HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminArea_WhenTenantAdmin_CallsNext()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "tenant-admin@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = false,
        });
        var authCtx = new CurrentAuthorizationContext();
        authCtx.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Admin,
        });

        var status = await InvokeGroupEndpointAsync(
            group => group.RequireAdminArea(), HttpMethod.Get,
            principal: principal,
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.OK);
    }

    // ── F001: site-admin tenant access requires break-glass ─────────────────

    [Fact]
    public async Task AdminArea_WhenSiteAdminTenantScopedWithoutBreakGlass_Returns403BreakGlass()
    {
        // Site-admin on a tenant subdomain with no active break-glass session: the middleware leaves
        // the authorization context at Role=None, so the endpoint must deny and route back to /admin.
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = true,
        });
        var authCtx = new CurrentAuthorizationContext(); // no break-glass → Role=None

        var response = await InvokeGroupEndpointResponseAsync(
            group => group.RequireAdminArea(), HttpMethod.Get,
            principal: principal,
            authContext: authCtx,
            currentTenant: TenantScope());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be(ApiErrorCodes.BreakGlassExpired);
    }

    [Fact]
    public async Task AdminArea_WhenSiteAdminTenantScopedWithBreakGlass_CallsNext()
    {
        // Active break-glass is folded into the authorization context as Role=Admin by the middleware.
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = true,
        });
        var authCtx = new CurrentAuthorizationContext();
        authCtx.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Admin,
        });

        var status = await InvokeGroupEndpointAsync(
            group => group.RequireAdminArea(), HttpMethod.Get,
            principal: principal,
            authContext: authCtx,
            currentTenant: TenantScope());

        status.Should().Be((int)HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminArea_WhenSiteAdminControlPlane_CallsNext()
    {
        // No tenant resolved (apex/admin host): bare site-admin access is preserved.
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = true,
        });

        var status = await InvokeGroupEndpointAsync(
            group => group.RequireAdminArea(), HttpMethod.Get,
            principal: principal,
            currentTenant: new CurrentTenant()); // HasTenant == false

        status.Should().Be((int)HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminArea_WhenGenuineTenantAdminTenantScoped_CallsNext()
    {
        // A real tenant Admin (not a site-admin) needs no break-glass.
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "tenant-admin@test.example",
            AuthProvider = AuthProvider.Keycloak,
            IsSiteAdmin = false,
        });
        var authCtx = new CurrentAuthorizationContext();
        authCtx.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Admin,
        });

        var status = await InvokeGroupEndpointAsync(
            group => group.RequireAdminArea(), HttpMethod.Get,
            principal: principal,
            authContext: authCtx,
            currentTenant: TenantScope());

        status.Should().Be((int)HttpStatusCode.OK);
    }
}
