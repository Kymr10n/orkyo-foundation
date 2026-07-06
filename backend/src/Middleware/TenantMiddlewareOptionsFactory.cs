using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Api.Middleware;

public static class TenantMiddlewareOptionsFactory
{
    public static TenantMiddlewareOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new TenantMiddlewareOptions();
        configuration.GetSection(ConfigKeys.TenantResolutionSection).Bind(options);
        return options;
    }
}
