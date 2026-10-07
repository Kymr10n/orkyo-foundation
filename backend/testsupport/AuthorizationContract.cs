using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// The authorization conformance check, shared so every host runs it against its own endpoint
/// graph: foundation against <c>FoundationWebApplicationFactory</c>, each product against
/// <c>WebApplicationFactory&lt;Program&gt;</c>. A product's mutating routes are only visible in the
/// product's host, so a test that runs in foundation alone cannot see an ungated SaaS write.
/// </summary>
/// <remarks>
/// The helper returns the offenders instead of asserting, so it carries no test-framework
/// dependency; the calling test asserts the list is empty with its own message.
/// </remarks>
public static class AuthorizationContract
{
    /// <summary>
    /// Foundation routes whose writes act on the caller's own data or happen before a tenant
    /// role exists, so they are intentionally outside the tenant role conventions. A product
    /// concatenates its own self-service prefixes to this list.
    /// </summary>
    public static readonly IReadOnlyList<string> FoundationSelfServicePrefixes =
    [
        "/api/auth",
        "/api/session",
        "/api/account",
        "/api/preferences",
        "/api/contact",
        "/api/feedback",
        "/api/announcements",   // user-facing: mark-as-read (note: /api/admin/announcements IS governed)
        "/api/invitations",
        // Calendar subscriptions are the caller's own feed tokens: every route
        // resolves the user from the principal and the revoke predicate is
        // scoped by user_id, so one user can never touch another's. The group requires tenant
        // membership but deliberately has no write gate (a viewer manages their own feeds), so
        // it carries no governed marker and stays on this list.
        "/api/calendar/subscriptions",
        // The browser's error beacon: anonymous by design (the login page can fail before any
        // session exists) and it changes nothing — one structured log event per report.
        "/api/client-errors",
    ];

    /// <summary>
    /// Foundation routes that answer anonymous callers on purpose: sign-in and account creation,
    /// links opened from an email, the calendar feed (its token is the credential), the public
    /// contact form and the version probe. A product concatenates its own anonymous routes.
    /// </summary>
    public static readonly IReadOnlyList<string> FoundationAnonymousRoutes =
    [
        "/api/account/confirm-activity",
        "/api/account/confirm-email",
        "/api/announcements/unsubscribe",
        "/api/auth/bff/callback",
        "/api/auth/bff/login",
        "/api/auth/bff/logout",
        "/api/auth/bff/me",
        "/api/auth/create-account",
        "/api/calendar/feed/{token}.ics",
        "/api/client-errors",
        "/api/contact/",
        "/api/invitations/accept",
        "/api/invitations/validate",
        "/api/version",
    ];

    /// <summary>
    /// Every mutating <c>/api</c> route in <paramref name="dataSource"/> that carries no
    /// <typeparamref name="TGoverned"/> metadata and does not start with one of
    /// <paramref name="selfServicePrefixes"/>, as "<c>METHODS /path</c>" strings, sorted.
    /// </summary>
    /// <typeparam name="TGoverned">
    /// The marker metadata the authorization conventions stamp (foundation's
    /// <c>AuthorizationGoverned</c>). Generic so this project does not reference the Web assembly.
    /// </typeparam>
    public static IReadOnlyList<string> FindUngovernedMutatingRoutes<TGoverned>(
        EndpointDataSource dataSource,
        IEnumerable<string> selfServicePrefixes)
        where TGoverned : class
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var prefixes = selfServicePrefixes.ToList();

        var ungoverned = new List<string>();
        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                          ?? (IReadOnlyList<string>)Array.Empty<string>();
            var isMutating = methods.Any(m =>
                HttpMethods.IsPost(m) || HttpMethods.IsPut(m) || HttpMethods.IsPatch(m) || HttpMethods.IsDelete(m));
            if (!isMutating) continue;

            var path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            if (!path.StartsWith("/api", StringComparison.Ordinal)) continue;
            if (prefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal))) continue;

            if (endpoint.Metadata.GetMetadata<TGoverned>() is null)
                ungoverned.Add($"{string.Join(",", methods)} {path}");
        }

        return ungoverned.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Every <c>/api/admin</c> route in <paramref name="dataSource"/>, of any verb, that carries no
    /// <typeparamref name="TGoverned"/> metadata, as "<c>METHODS /path</c>" strings, sorted.
    /// <see cref="FindUngovernedMutatingRoutes{TGoverned}"/> checks writes only; under
    /// <c>/api/admin</c> a read is as sensitive as a write, so an admin GET that forgets its gate
    /// must fail too.
    /// </summary>
    public static IReadOnlyList<string> FindUngovernedAdminRoutes<TGoverned>(EndpointDataSource dataSource)
        where TGoverned : class
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        return dataSource.Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint => (endpoint, path: "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/')))
            .Where(e => e.path == "/api/admin" || e.path.StartsWith("/api/admin/", StringComparison.Ordinal))
            .Where(e => e.endpoint.Metadata.GetMetadata<TGoverned>() is null)
            .Select(e => $"{string.Join(",", e.endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.path}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every <c>/api</c> route in <paramref name="dataSource"/>, of any verb, whose authentication
    /// is not declared: it carries neither <see cref="IAuthorizeData"/> nor
    /// <see cref="IAllowAnonymous"/>, or it allows anonymous callers but its path is not in
    /// <paramref name="anonymousRoutes"/>. Returned as "<c>METHODS /path</c>" strings, sorted.
    /// </summary>
    /// <remarks>
    /// The host has no fallback policy, so a route with no metadata answers anonymous callers.
    /// <paramref name="anonymousRoutes"/> holds exact route templates or, ending in <c>/</c>, prefixes.
    /// </remarks>
    public static IReadOnlyList<string> FindUnauthenticatedRoutes(
        EndpointDataSource dataSource,
        IEnumerable<string> anonymousRoutes)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var allowed = anonymousRoutes.ToList();

        bool IsAllowed(string path) => allowed.Any(a => a.EndsWith('/')
            ? path.StartsWith(a, StringComparison.Ordinal)
            : path == a);

        return dataSource.Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint => (endpoint, path: "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/')))
            .Where(e => e.path == "/api" || e.path.StartsWith("/api/", StringComparison.Ordinal))
            .Where(e => e.endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                ? !IsAllowed(e.path)
                : e.endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(e => $"{string.Join(",", e.endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.path}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The assertion message every host uses, so the guidance reads the same everywhere.</summary>
    public static string Explain(IReadOnlyList<string> ungoverned) =>
        "These mutating /api routes have no authorization convention. Declare one of the group "
        + "conventions (RequireMemberReadEditorWrite / RequireMemberReadAdminWrite / RequireAdminArea), "
        + "mark a non-mutating POST with AllowMemberWrite, or allow-list a genuine self-service route:\n  "
        + string.Join("\n  ", ungoverned);
}
