using Api.Security;

namespace Orkyo.Foundation.Tests.PlatformApi;

/// <summary>
/// The one thing every MCP tool-class test needs: an authorization context standing in for a token
/// of a given scope. Extracted so the four tool suites share one definition of "what a read-only
/// token looks like" rather than each re-deriving it — if the scope-to-role mapping ever changes,
/// it changes here once.
/// </summary>
internal static class McpToolTestContext
{
    /// <param name="role">
    /// <see cref="TenantRole.Editor"/> stands in for a <c>schedule:write</c> token,
    /// <see cref="TenantRole.Viewer"/> for a read-only one — the mapping
    /// <see cref="PlatformApiScopes.ScopeToRole"/> makes in production.
    /// </param>
    public static CurrentAuthorizationContext ForRole(TenantRole role)
    {
        var auth = new CurrentAuthorizationContext();
        auth.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = role,
        });
        return auth;
    }
}
