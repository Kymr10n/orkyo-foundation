using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

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

    private static async Task<string> AdminTokenAsync()
    {
        var email = $"quota_admin_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, "Quota Admin", TenantSlug, "admin", active: true);
        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Quota Admin",
            TenantId = "00000000-0000-0000-0000-000000000001",
            TenantSlug,
            IsTenantAdmin = true,
            Role = "admin",
        };
        return Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokenData)));
    }

    [Fact]
    public async Task GetQuotas_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/settings/quotas/");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetQuotas_AsAdmin_ReachesHandler_AndReturnsUsage()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings/quotas/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "the quota route must be mapped by the test factory (regression guard for the MapQuotaEndpoints omission)");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
