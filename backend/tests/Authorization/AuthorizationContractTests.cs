using Api.Endpoints.Admin;
using Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Authorization;

/// <summary>
/// The self-guarding authorization conformance test. It enumerates the live endpoint graph and
/// asserts that <b>every mutating <c>/api</c> route is governed by an authorization convention</b>
/// (one of the <c>Require*</c> group conventions, <c>RequireSiteAdmin</c>, or an explicit role
/// filter — all of which stamp <see cref="AuthorizationGoverned"/>). The only exceptions are genuine
/// self-service / pre-login surfaces, allow-listed by prefix in
/// <see cref="AuthorizationContract.FoundationSelfServicePrefixes"/>.
///
/// This is the guardrail that stops a future change (human or AI) from shipping an ungated write:
/// add a new POST/PUT/PATCH/DELETE without declaring a convention and this test fails. See
/// docs/authorization.md for the contract. The walk itself lives in
/// <see cref="AuthorizationContract"/> (Orkyo.Foundation.TestSupport) so each product runs the
/// same check against its own host, where its own routes are visible.
/// </summary>
[Collection("Database collection")]
public class AuthorizationContractTests
{
    private readonly DatabaseFixture _fixture;

    public AuthorizationContractTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void EveryMutatingApiRoute_IsGovernedByAnAuthorizationConvention()
    {
        var dataSource = _fixture.Factory.Services.GetRequiredService<EndpointDataSource>();

        var ungoverned = AuthorizationContract.FindUngovernedMutatingRoutes<AuthorizationGoverned>(
            dataSource, AuthorizationContract.FoundationSelfServicePrefixes);

        Assert.True(ungoverned.Count == 0, AuthorizationContract.Explain(ungoverned));
    }

    [Fact]
    public void EveryAdminRoute_ReadsIncluded_IsGoverned()
    {
        var dataSource = _fixture.Factory.Services.GetRequiredService<EndpointDataSource>();

        var ungoverned = AuthorizationContract.FindUngovernedAdminRoutes<AuthorizationGoverned>(dataSource);

        Assert.True(ungoverned.Count == 0,
            "These /api/admin routes have no gate. Map them with MapSiteAdminGroup():\n  "
            + string.Join("\n  ", ungoverned));
    }

    [Fact]
    public void AnUngatedAdminRead_IsReported()
    {
        // The check itself: a GET under /api/admin with no convention is caught, a gated one is not.
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/api/admin/open", () => "x");
        app.MapSiteAdminGroup().MapGet("/closed", () => "x");
        var dataSource = new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources);

        AuthorizationContract.FindUngovernedAdminRoutes<AuthorizationGoverned>(dataSource)
            .Should().Equal("GET /api/admin/open");
    }
}
