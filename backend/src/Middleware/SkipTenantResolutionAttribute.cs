using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace Api.Middleware;

/// <summary>
/// Marks an endpoint or group as not requiring tenant context.
/// Apply to routes that operate at the control-plane level (cross-tenant)
/// or that are fully public and have no tenant relationship.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipTenantResolutionAttribute : Attribute, ISkipTenantResolution { }

public interface ISkipTenantResolution { }

/// <summary>
/// Extension methods for applying the standard infrastructure-endpoint metadata chain:
/// anonymous access + skip tenant resolution.
/// </summary>
public static class InfrastructureEndpointExtensions
{
    /// <summary>
    /// Marks the endpoint as fully public and not requiring tenant context.
    /// Equivalent to <c>.AllowAnonymous().WithMetadata(new SkipTenantResolutionAttribute())</c>.
    /// Use on health, metrics, info, and swagger endpoints.
    /// </summary>
    public static TBuilder AsInfrastructureEndpoint<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.AllowAnonymous().WithMetadata(new SkipTenantResolutionAttribute());
}