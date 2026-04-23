namespace Api.Services;

/// <summary>
/// Pure builder for outbound email token links of the form
/// <c>{appBaseUrl}/{path}?{queryParam}={url-escaped token}</c>.
/// Structurally identical in multi-tenant SaaS and single-tenant Community
/// deployments: both products email verification/reset/invitation/lifecycle
/// links assembled from a configured public base URL and a per-flow token.
/// </summary>
public static class EmailTokenLinkBuilder
{
    /// <summary>
    /// Build a token link. Only <paramref name="token"/> is URL-escaped, mirroring
    /// pre-existing SaaS behavior — <paramref name="path"/> and
    /// <paramref name="queryParam"/> are caller-controlled literals.
    /// </summary>
    public static string Build(string appBaseUrl, string path, string queryParam, string token)
    {
        return $"{appBaseUrl}/{path}?{queryParam}={Uri.EscapeDataString(token)}";
    }
}
