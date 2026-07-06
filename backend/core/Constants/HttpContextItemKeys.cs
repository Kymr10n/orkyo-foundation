namespace Api.Constants;

/// <summary>
/// Keys for well-known <c>HttpContext.Items</c> entries. The product middleware
/// (SaaS <c>TenantMiddleware</c>, Community <c>SingleTenantMiddleware</c>) writes these;
/// foundation code (<c>TenantContextAccessExtensions</c>, <c>ContextEnrichmentMiddleware</c>)
/// reads them. Writer and reader live in different repos, so both sides must reference
/// these constants — a raw literal on either side breaks tenant resolution at runtime
/// with no compile error.
/// </summary>
public static class HttpContextItemKeys
{
    public const string TenantContext = "TenantContext";
    public const string OrgContext = "OrgContext";
}
