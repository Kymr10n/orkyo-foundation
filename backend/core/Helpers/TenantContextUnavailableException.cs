namespace Api.Helpers;

/// <summary>
/// Thrown when a service that needs an <see cref="Services.OrgContext"/> is resolved in a scope
/// where no tenant was resolved. A programming error, not a client error: the endpoint either
/// skipped tenant resolution and must not touch tenant data, or the middleware order is wrong.
/// Maps to a 500 with a stable <c>code</c> so it is recognisable in logs and never mistaken
/// for a data problem.
/// </summary>
public sealed class TenantContextUnavailableException : Exception
{
    public const string Code = "tenant_context_unavailable";

    public TenantContextUnavailableException()
        : base("No tenant is resolved for this request, but a tenant-scoped service was requested. "
               + "Either the route skips tenant resolution and must not use tenant data, or the "
               + "tenant middleware runs after the service was resolved.")
    {
    }
}
