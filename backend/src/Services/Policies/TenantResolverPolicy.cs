using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Api.Services;

public static class TenantResolverPolicy
{
    public static TimeSpan CacheTtl => TimePolicyConstants.CacheTtl;

    public static string ResolveControlPlaneConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString(ConfigKeys.ConnectionStringControlPlane)
            ?? throw new InvalidOperationException("ControlPlane connection string not configured");
    }

    public static DateTime GetCacheExpiryUtc(DateTime utcNow)
    {
        return utcNow.Add(CacheTtl);
    }
}