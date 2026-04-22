using Api.Services;
using Microsoft.AspNetCore.Http;

namespace Api.Middleware;

public static class TenantContextAccessExtensions
{
    public static TenantContext GetTenantContext(this HttpContext context)
    {
        return context.Items["TenantContext"] as TenantContext
            ?? throw new InvalidOperationException("Tenant context not available");
    }

    public static OrgContext GetOrgContext(this HttpContext context)
    {
        return context.Items["OrgContext"] as OrgContext
            ?? throw new InvalidOperationException("Org context not available");
    }
}