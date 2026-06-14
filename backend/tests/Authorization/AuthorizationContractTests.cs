using Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Authorization;

/// <summary>
/// The self-guarding authorization conformance test. It enumerates the live endpoint graph and
/// asserts that <b>every mutating <c>/api</c> route is governed by an authorization convention</b>
/// (one of the <c>Require*</c> group conventions, <c>RequireSiteAdmin</c>, or an explicit role
/// filter — all of which stamp <see cref="AuthorizationGoverned"/>). The only exceptions are genuine
/// self-service / pre-login surfaces, allow-listed by prefix below.
///
/// This is the guardrail that stops a future change (human or AI) from shipping an ungated write:
/// add a new POST/PUT/PATCH/DELETE without declaring a convention and this test fails. See
/// docs/authorization.md for the contract.
/// </summary>
[Collection("Database collection")]
public class AuthorizationContractTests
{
    private readonly DatabaseFixture _fixture;

    public AuthorizationContractTests(DatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Writes here act on the caller's own data or happen before a tenant role exists, so they are
    /// intentionally outside the tenant role conventions. Anything else that mutates must be governed.
    /// </summary>
    private static readonly string[] SelfServicePrefixes =
    {
        "/api/auth",
        "/api/session",
        "/api/account",
        "/api/preferences",
        "/api/contact",
        "/api/feedback",
        "/api/announcements",   // user-facing: mark-as-read (note: /api/admin/announcements IS governed)
        "/api/invitations",
    };

    [Fact]
    public void EveryMutatingApiRoute_IsGovernedByAnAuthorizationConvention()
    {
        var dataSource = _fixture.Factory.Services.GetRequiredService<EndpointDataSource>();

        var ungoverned = new List<string>();
        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                          ?? (IReadOnlyList<string>)Array.Empty<string>();
            var isMutating = methods.Any(m =>
                HttpMethods.IsPost(m) || HttpMethods.IsPut(m) || HttpMethods.IsPatch(m) || HttpMethods.IsDelete(m));
            if (!isMutating) continue;

            var path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            if (!path.StartsWith("/api")) continue;
            if (SelfServicePrefixes.Any(prefix => path.StartsWith(prefix))) continue;

            if (endpoint.Metadata.GetMetadata<AuthorizationGoverned>() is null)
                ungoverned.Add($"{string.Join(",", methods)} {path}");
        }

        Assert.True(ungoverned.Count == 0,
            "These mutating /api routes have no authorization convention. Declare one of the group "
            + "conventions (RequireMemberReadEditorWrite / RequireMemberReadAdminWrite / RequireAdminArea), "
            + "mark a non-mutating POST with AllowMemberWrite, or allow-list a genuine self-service route:\n  "
            + string.Join("\n  ", ungoverned.OrderBy(x => x)));
    }
}
