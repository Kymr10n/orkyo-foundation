using System.Net;
using System.Reflection;
using System.Text.Json;
using Api.Constants;
using Api.Middleware;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
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

    private static async Task<HttpResponseMessage> InvokeFilteredEndpointResponseAsync(
        Action<RouteHandlerBuilder> applyFilter,
        ICurrentPrincipal? principal = null,
        IAuthorizationContext? authContext = null,
        ICurrentTenant? currentTenant = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();

        if (principal != null)
            builder.Services.AddSingleton<ICurrentPrincipal>(principal);
        if (authContext != null)
            builder.Services.AddSingleton<IAuthorizationContext>(authContext);
        if (currentTenant != null)
            builder.Services.AddSingleton<ICurrentTenant>(currentTenant);

        var app = builder.Build();
        var route = app.MapGet("/test", () => Results.Ok());
        applyFilter(route);

        await app.StartAsync();
        try
        {
            return await app.GetTestClient().GetAsync("/test");
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

    // ── RequireRole ────────────────────────────────────────────────────────

    [Fact]
    public async Task RequireRole_WhenNoAuthContext_Returns401()
    {
        // No principal, no auth context → BuildMembershipDenial → unauthenticated → 401
        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireRole(TenantRole.Editor));

        status.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequireRole_WhenNotMember_Returns403()
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
            route => route.RequireRole(TenantRole.Editor),
            principal: principal,
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequireRole_WhenRoleNotAllowed_Returns403()
    {
        var authCtx = new CurrentAuthorizationContext();
        authCtx.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Viewer,
        });

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireRole(TenantRole.Editor),
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequireRole_WhenRoleAllowed_CallsNext()
    {
        var authCtx = new CurrentAuthorizationContext();
        authCtx.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Editor,
        });

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireRole(TenantRole.Editor),
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

    // ── RequireAdminAccess ─────────────────────────────────────────────────

    [Fact]
    public async Task RequireAdminAccess_WhenNoPrincipal_Returns401()
    {
        var status = await InvokeFilteredEndpointAsync(route => route.RequireAdminAccess());

        status.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequireAdminAccess_WhenAuthenticatedWithNoAdminAccess_Returns403()
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

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireAdminAccess(),
            principal: principal,
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequireAdminAccess_WhenSiteAdmin_CallsNext()
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
            route => route.RequireAdminAccess(),
            principal: principal);

        status.Should().Be((int)HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequireAdminAccess_WhenTenantAdmin_CallsNext()
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

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireAdminAccess(),
            principal: principal,
            authContext: authCtx);

        status.Should().Be((int)HttpStatusCode.OK);
    }

    // ── F001: site-admin tenant access requires break-glass ─────────────────

    [Fact]
    public async Task RequireAdminAccess_WhenSiteAdminTenantScopedWithoutBreakGlass_Returns403BreakGlass()
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

        var response = await InvokeFilteredEndpointResponseAsync(
            route => route.RequireAdminAccess(),
            principal: principal,
            authContext: authCtx,
            currentTenant: TenantScope());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be(ApiErrorCodes.BreakGlassExpired);
    }

    [Fact]
    public async Task RequireAdminAccess_WhenSiteAdminTenantScopedWithBreakGlass_CallsNext()
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

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireAdminAccess(),
            principal: principal,
            authContext: authCtx,
            currentTenant: TenantScope());

        status.Should().Be((int)HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequireAdminAccess_WhenSiteAdminControlPlane_CallsNext()
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

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireAdminAccess(),
            principal: principal,
            currentTenant: new CurrentTenant()); // HasTenant == false

        status.Should().Be((int)HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequireAdminAccess_WhenGenuineTenantAdminTenantScoped_CallsNext()
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

        var status = await InvokeFilteredEndpointAsync(
            route => route.RequireAdminAccess(),
            principal: principal,
            authContext: authCtx,
            currentTenant: TenantScope());

        status.Should().Be((int)HttpStatusCode.OK);
    }
}
