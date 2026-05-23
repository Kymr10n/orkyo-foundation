namespace Api.Services;

/// <summary>
/// Scope-enforcement policy for tenant/site settings writes.
///
/// The platform partitions <see cref="TenantSettingDescriptorCatalog"/> keys into
/// "site" scope (platform-wide overrides such as brute-force/rate-limit policy)
/// and "tenant" scope (per-tenant overrides such as branding/working hours).
/// A write must originate from the scope the key belongs to: a tenant-scoped
/// key cannot be mutated from the site-admin context, and a site-scoped key
/// cannot be mutated from a tenant context.
///
/// The policy is pure (no IO, no logging) and applies identically to
/// multi-tenant SaaS and single-tenant Community deployments; composition
/// layers retain repository/cache orchestration and supply the scope bit.
/// </summary>
public static class TenantSettingsScopePolicy
{
    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="key"/> is not
    /// writable from the caller's scope. <paramref name="operationVerb"/> is
    /// embedded in the error message (e.g. "modified" or "reset") so callers
    /// can preserve their original wire-message contract.
    /// </summary>
    public static void EnsureWritableInScope(string key, bool isSiteContext, string operationVerb)
    {
        var isSiteKey = TenantSettingDescriptorCatalog.SiteKeys.Contains(key);

        if (isSiteContext && !isSiteKey)
        {
            throw new ArgumentException(
                $"Setting '{key}' is tenant-scoped and cannot be {operationVerb} from the site context");
        }

        if (!isSiteContext && isSiteKey)
        {
            throw new ArgumentException(
                $"Setting '{key}' is site-scoped and cannot be {operationVerb} from a tenant context");
        }
    }
}
