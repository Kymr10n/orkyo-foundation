using Api.Endpoints.Admin;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkyo.Foundation.Tests.Middleware;

/// <summary>
/// Membership by default: an authenticated non-member on a tenant-scoped route is denied
/// before the endpoint, unless the endpoint declares its way past the gate.
/// </summary>
public class TenantMembershipMiddlewareTests
{
    private static readonly object[] TenantRoute = [new EndpointNameMetadata("tenant-route")];

    private static DefaultHttpContext CreateContext(bool authenticated, bool hasTenant, TenantRole role, params object[] endpointMetadata)
    {
        var principal = new CurrentPrincipal();
        if (authenticated)
            principal.SetContext(new PrincipalContext { UserId = Guid.NewGuid(), Email = "p@example.com", DisplayName = "P", AuthProvider = AuthProvider.Keycloak, IsSiteAdmin = false });
        var tenant = new CurrentTenant();
        var authorization = new CurrentAuthorizationContext();
        if (hasTenant)
        {
            var tenantContext = new TenantContext { TenantId = Guid.NewGuid(), TenantSlug = "acme", TenantDbConnectionString = "Host=x", Status = "active" };
            tenant.SetContext(tenantContext);
            authorization.SetContext(new AuthorizationContext { TenantId = tenantContext.TenantId, TenantSlug = "acme", Role = role });
        }
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentPrincipal>(principal);
        services.AddSingleton<ICurrentTenant>(tenant);
        services.AddSingleton<IAuthorizationContext>(authorization);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        if (endpointMetadata.Length > 0)
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(endpointMetadata), "route"));
        return context;
    }

    private static async Task<(bool reachedEndpoint, int status)> RunAsync(HttpContext context)
    {
        var reached = false;
        var middleware = new TenantMembershipMiddleware(_ => { reached = true; return Task.CompletedTask; }, NullLogger<TenantMembershipMiddleware>.Instance);
        var services = context.RequestServices;
        await middleware.InvokeAsync(context,
            services.GetRequiredService<ICurrentPrincipal>(),
            services.GetRequiredService<ICurrentTenant>(),
            services.GetRequiredService<IAuthorizationContext>());
        return (reached, context.Response.StatusCode);
    }

    [Fact]
    public async Task NonMember_OnATenantRoute_IsDeniedBeforeTheEndpoint()
    {
        var (reached, status) = await RunAsync(CreateContext(authenticated: true, hasTenant: true, TenantRole.None, TenantRoute));

        reached.Should().BeFalse("membership is the default on a tenant-scoped route");
        status.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [MemberData(nameof(OptOutMetadata))]
    public async Task NonMember_OnARouteThatOptsOut_ReachesTheEndpoint(object metadata)
    {
        var (reached, status) = await RunAsync(CreateContext(authenticated: true, hasTenant: true, TenantRole.None, metadata));

        reached.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    public static TheoryData<object> OptOutMetadata => new()
    {
        new AllowAnonymousAttribute(),
        new SkipTenantResolutionAttribute(),
        new AllowNonMemberAttribute(),
    };

    [Fact]
    public async Task Member_OnATenantRoute_ReachesTheEndpoint()
    {
        var (reached, _) = await RunAsync(CreateContext(authenticated: true, hasTenant: true, TenantRole.Viewer, TenantRoute));

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task AnonymousCaller_IsNotTheGatesConcern()
    {
        // RequireAuthorization answers an anonymous caller; the gate only judges members.
        var (reached, _) = await RunAsync(CreateContext(authenticated: false, hasTenant: true, TenantRole.None, TenantRoute));

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task NoTenantResolved_PassesThrough()
    {
        var (reached, _) = await RunAsync(CreateContext(authenticated: true, hasTenant: false, TenantRole.None, TenantRoute));

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task UnmatchedRoute_PassesThrough()
    {
        var (reached, _) = await RunAsync(CreateContext(authenticated: true, hasTenant: true, TenantRole.None));

        reached.Should().BeTrue("a 404 is the router's answer, not a membership denial");
    }

    [Fact]
    public void SiteAdminRoutes_OptOut_ByConstruction()
    {
        // Community resolves its single tenant on every request, admin routes included; a site
        // admin need not be a member to reach the control plane.
        var app = WebApplication.CreateBuilder().Build();
        app.MapSiteAdminGroup().MapGet("/x", () => "x");
        app.MapGet("/api/admin/y", () => "y").RequireSiteAdmin();
        var endpoints = new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources).Endpoints;

        endpoints.Should().HaveCount(2).And.OnlyContain(e => e.Metadata.GetMetadata<IAllowNonMember>() != null);
    }
}
