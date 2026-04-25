using System.Text.RegularExpressions;

namespace Api.Services;

public enum TenantCreationDecision
{
    Allowed,
    ReservedSlug,
    InvalidSlugFormat,
    UserAlreadyOwnsTenant
}

public static partial class TenantCreationPolicy
{
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        // Infrastructure subdomains
        "www", "api", "auth", "admin", "mail", "smtp", "imap", "pop",
        "grafana", "prometheus", "loki", "monitoring", "status",
        // Environments
        "staging", "dev", "test", "preview", "sandbox", "demo", "beta",
        // Common phishing targets
        "login", "signin", "signup", "register", "account", "billing",
        "support", "help", "security", "dashboard", "app", "portal",
        // Technical
        "cdn", "assets", "static", "media", "files", "upload",
        "ws", "wss", "socket", "webhook", "callback",
        // Brand protection
        "orkyo", "orkyo-admin", "orkyo-api", "orkyo-auth"
    };

    [GeneratedRegex("^[a-z][a-z0-9-]*[a-z0-9]$|^[a-z]{3}$")]
    private static partial Regex SlugRegex();

    public static TenantCreationDecision EvaluateSlug(string slug)
    {
        if (ReservedSlugs.Contains(slug))
            return TenantCreationDecision.ReservedSlug;

        if (string.IsNullOrEmpty(slug) || slug.Length < 3 || slug.Length > 63)
            return TenantCreationDecision.InvalidSlugFormat;

        if (!SlugRegex().IsMatch(slug))
            return TenantCreationDecision.InvalidSlugFormat;

        return TenantCreationDecision.Allowed;
    }

    public static TenantCreationDecision EvaluateOwnershipEligibility(bool canUserCreateTenant)
    {
        return canUserCreateTenant
            ? TenantCreationDecision.Allowed
            : TenantCreationDecision.UserAlreadyOwnsTenant;
    }
}
