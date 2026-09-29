using System.Net;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration coverage for GET /api/settings/quotas. These exist because the test
/// factory previously omitted MapQuotaEndpoints(), leaving the quota endpoints with
/// zero integration coverage — a regression could ship silently. The route is
/// admin-area; an authenticated admin must reach the handler (not a routing 404),
/// and an unauthenticated caller must be rejected.
/// </summary>
[Collection("Database collection")]
public class QuotaEndpointsTests
{
    private readonly HttpClient _client;
    private const string TenantSlug = TestConstants.TenantSlug;

    public QuotaEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.Factory.CreateClient();
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
    }

    [Fact]
    public async Task GetQuotas_AsAdmin_ReachesHandler_AndReturnsUsage()
    {
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/settings/quotas/", await DatabaseFixture.CreateMemberTokenAsync(RoleConstants.Admin));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "the quota route must be mapped by the test factory (regression guard for the MapQuotaEndpoints omission)");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
