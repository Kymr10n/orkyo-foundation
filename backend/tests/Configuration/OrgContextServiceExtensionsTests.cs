using Api.Configuration;
using Api.Constants;
using Api.Helpers;
using Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Configuration;

/// <summary>
/// The one OrgContext registration both products call. It reads what the tenant middleware
/// stored on the request and refuses, with a typed exception, to hand out a tenant-scoped
/// context where no tenant was resolved — the sentinel with an empty connection string that
/// the SaaS Program.cs used to return is what this replaces.
/// </summary>
public sealed class OrgContextServiceExtensionsTests
{
    private static readonly TenantContext Tenant = new()
    {
        TenantId = Guid.Parse("00000000-0000-0000-0000-00000000aaaa"),
        TenantSlug = "acme",
        TenantDbConnectionString = "Host=localhost;Database=acme",
        Status = "active",
    };

    private static (IServiceProvider Services, DefaultHttpContext Http) Build(bool withHttpContext = true)
    {
        var http = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = withHttpContext ? http : null };

        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddOrgContextFromHttpContext();
        return (services.BuildServiceProvider(), http);
    }

    [Fact]
    public void ResolvesTheOrgContext_FromTheTenantContextItem()
    {
        var (services, http) = Build();
        http.Items[HttpContextItemKeys.TenantContext] = Tenant;

        using var scope = services.CreateScope();
        var org = scope.ServiceProvider.GetRequiredService<OrgContext>();

        org.OrgId.Should().Be(Tenant.TenantId);
        org.OrgSlug.Should().Be("acme");
        org.DbConnectionString.Should().Be(Tenant.TenantDbConnectionString);
    }

    [Fact]
    public void PrefersTheDerivedOrgContextItem_WhenTheMiddlewareStoredOne()
    {
        var (services, http) = Build();
        var stored = new OrgContext { OrgId = Guid.NewGuid(), OrgSlug = "stored", DbConnectionString = "Host=x" };
        http.Items[HttpContextItemKeys.TenantContext] = Tenant;
        http.Items[HttpContextItemKeys.OrgContext] = stored;

        using var scope = services.CreateScope();

        scope.ServiceProvider.GetRequiredService<OrgContext>().Should().BeSameAs(stored);
    }

    [Fact]
    public void TheAccessorIsNull_AndOrgContextThrows_WhenNoTenantWasResolved()
    {
        var (services, _) = Build();

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOrgContextAccessor>().Current.Should().BeNull(
            "a site-admin or skip-tenant request has no tenant, and services that serve both scopes branch on null");

        var act = () => scope.ServiceProvider.GetRequiredService<OrgContext>();

        act.Should().Throw<TenantContextUnavailableException>(
            "a tenant-only service must fail typed here, never open a connection with an empty string");
    }

    [Fact]
    public void TheAccessorIsNull_OutsideAnHttpRequest()
    {
        var (services, _) = Build(withHttpContext: false);

        using var scope = services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOrgContextAccessor>().Current.Should().BeNull();
    }

    [Fact]
    public async Task TheExceptionMapsToA500_WithAStableCode()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var handled = await new AppExceptionHandler().TryHandleAsync(ctx, new TenantContextUnavailableException(), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        ctx.Response.Body.Position = 0;
        var body = await System.Text.Json.JsonDocument.ParseAsync(ctx.Response.Body);
        body.RootElement.GetProperty("code").GetString().Should().Be(TenantContextUnavailableException.Code);
    }
}
