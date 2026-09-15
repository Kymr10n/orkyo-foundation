namespace Api.Services;

/// <summary>
/// The current request's <see cref="OrgContext"/>, or <c>null</c> when no tenant is resolved
/// (a site-admin surface, a <c>[SkipTenantResolution]</c> route, a background scope).
/// </summary>
/// <remarks>
/// Services that legitimately run in both worlds — <see cref="TenantSettingsService"/> serves
/// site scope when there is no tenant — depend on this and branch on <c>null</c>. Services that
/// only make sense inside a tenant keep depending on <see cref="OrgContext"/> directly; the
/// registration in <c>AddOrgContextFromHttpContext</c> throws a typed
/// <c>TenantContextUnavailableException</c> for them instead of handing out a sentinel with an
/// empty connection string.
/// </remarks>
public interface IOrgContextAccessor
{
    OrgContext? Current { get; }
}
