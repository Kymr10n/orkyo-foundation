using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Api.Middleware;

public static class TenantMiddlewareOptionsFactory
{
    public static TenantMiddlewareOptions FromConfiguration(IConfiguration configuration)
    {
        return new TenantMiddlewareOptions
        {
            BaseDomain = configuration[ConfigKeys.TenantResolutionBaseDomain],
            AllowTenantHeader = bool.TryParse(
                configuration[ConfigKeys.TenantResolutionAllowTenantHeader],
                out var allow) && allow,
        };
    }
}