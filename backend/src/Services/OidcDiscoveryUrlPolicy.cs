namespace Api.Services;

/// <summary>
/// Pure helpers for OIDC discovery URL composition and probe-status
/// interpretation used by diagnostics endpoints. Structurally identical in
/// multi-tenant SaaS and single-tenant Community deployments because both
/// products use the same OIDC discovery contract.
/// </summary>
public static class OidcDiscoveryUrlPolicy
{
    /// <summary>Stable status string emitted when no authority is configured.</summary>
    public const string NotConfiguredStatus = "not-configured";

    /// <summary>Stable status string emitted when discovery returns a successful HTTP response.</summary>
    public const string ConnectedStatus = "connected";

    /// <summary>Stable status string emitted when discovery is unreachable or returns a non-success HTTP response.</summary>
    public const string UnreachableStatus = "unreachable";

    /// <summary>
    /// Compose the discovery document URL for an OIDC authority by appending
    /// <c>/.well-known/openid-configuration</c>. A single trailing slash on the
    /// authority is trimmed so the result has no double slash. Returns
    /// <c>null</c> for null/whitespace authority values.
    /// </summary>
    public static string? BuildDiscoveryUrl(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority)) return null;
        return $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
    }

    /// <summary>
    /// Map an HTTP discovery probe outcome to the canonical diagnostics
    /// status string. <paramref name="isSuccessStatusCode"/> follows
    /// <c>HttpResponseMessage.IsSuccessStatusCode</c> semantics.
    /// </summary>
    public static string InterpretProbeOutcome(bool isSuccessStatusCode) =>
        isSuccessStatusCode ? ConnectedStatus : UnreachableStatus;
}
