using Api.Constants;
using Api.Helpers;
using Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Configuration;

/// <summary>
/// One registration of the request-scoped <see cref="OrgContext"/> for every edition, in place
/// of a hand-rolled factory per <c>Program.cs</c>. The tenant middleware (SaaS
/// <c>TenantMiddleware</c>, Community <c>SingleTenantMiddleware</c>) puts the resolved
/// <see cref="TenantContext"/> — and the derived <see cref="OrgContext"/> — into
/// <c>HttpContext.Items</c>; this reads them back.
/// </summary>
public static class OrgContextServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IOrgContextAccessor"/> (nullable, for services that serve both a
    /// tenant and a site-admin scope) and <see cref="OrgContext"/> (throws
    /// <see cref="TenantContextUnavailableException"/> when no tenant is resolved, so a
    /// tenant-only service can never open a connection with an empty connection string).
    /// </summary>
    public static IServiceCollection AddOrgContextFromHttpContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IOrgContextAccessor, HttpContextOrgContextAccessor>();
        services.AddScoped<OrgContext>(sp =>
            sp.GetRequiredService<IOrgContextAccessor>().Current
            ?? throw new TenantContextUnavailableException());
        return services;
    }
}

/// <summary>
/// Reads the org context the tenant middleware stored on the request: the derived
/// <see cref="OrgContext"/> item when present, else the <see cref="TenantContext"/> item.
/// </summary>
internal sealed class HttpContextOrgContextAccessor(IHttpContextAccessor httpContextAccessor) : IOrgContextAccessor
{
    public OrgContext? Current
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context is null) return null;

            if (context.Items.TryGetValue(HttpContextItemKeys.OrgContext, out var org) && org is OrgContext orgContext)
                return orgContext;

            if (context.Items.TryGetValue(HttpContextItemKeys.TenantContext, out var tenant) && tenant is TenantContext tenantContext)
                return OrgContextExtensions.FromTenant(tenantContext);

            return null;
        }
    }
}
