using Microsoft.Extensions.Configuration;

namespace Api.Middleware;

public static class TenantMiddlewareOptionsFactory
{
    public static TenantMiddlewareOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new TenantMiddlewareOptions();
        configuration.GetSection("TenantResolution").Bind(options);
        return options;
    }
}