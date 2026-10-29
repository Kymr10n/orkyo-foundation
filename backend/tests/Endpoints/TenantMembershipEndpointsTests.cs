using System.Net;
using System.Net.Http.Json;
using Api.Models;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// GET /api/tenants/memberships: foundation owns the route and the response shape; the test
/// host supplies the control-plane query SaaS registers.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Database)]
[Collection("Database collection")]
public class TenantMembershipEndpointsTests
{
    private readonly HttpClient _client;

    public TenantMembershipEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.Factory.CreateClient();
    }

    [Fact]
    public async Task Get_AsMember_ReturnsTheMembership()
    {
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/tenants/memberships",
            await DatabaseFixture.CreateMemberTokenAsync(RoleConstants.Editor));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var memberships = await response.Content.ReadFromJsonAsync<List<TenantMembershipResponse>>();
        memberships.Should().ContainSingle(m => m.TenantId == TestConstants.TenantId
            && m.Role == RoleConstants.Editor
            && m.Status == MembershipStatusConstants.Active);
    }

    [Fact]
    public async Task Get_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/api/tenants/memberships");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_UsesTheCamelCaseShapeTheFrontendReads()
    {
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/tenants/memberships",
            await DatabaseFixture.CreateMemberTokenAsync(RoleConstants.Viewer));

        var json = await (await _client.SendAsync(request)).Content.ReadAsStringAsync();

        // The frontend's AccountMembership (tenant-account-api.ts) reads these names.
        foreach (var field in new[] { "tenantId", "tenantSlug", "tenantDisplayName", "tenantStatus", "role", "status", "isOwner", "joinedAt" })
            json.Should().Contain($"\"{field}\":");
    }
}
