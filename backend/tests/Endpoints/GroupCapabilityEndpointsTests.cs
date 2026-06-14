using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Endpoints;
using Api.Models;
using Api.Repositories;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Tests for Group Capability CRUD endpoints.
/// Routes: /api/resource-groups/{groupId}/capabilities
/// Requires authentication and tenant membership.
/// </summary>
[Collection("Database collection")]
public class GroupCapabilityEndpointsTests
{
    private readonly HttpClient _client;
    private readonly HttpClient _unauthenticatedClient;

    public GroupCapabilityEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        _unauthenticatedClient = databaseFixture.Factory.CreateClient();
    }

    // The test group is space-typed, and the applicability guard only admits a criterion that is
    // explicitly applicable to space (its open-world fallback is off once the space type has any
    // applicability rows, which other tests in the shared DB commit). Picking from the shared pool
    // is order-dependent and flaky, so each CRUD test mints its own space-applicable criterion.
    private async Task<CriterionInfo> CreateSpaceCriterionAsync(CriterionDataType dataType)
    {
        var create = new CreateCriterionRequest
        {
            Name = $"grp_space_{dataType}_{Guid.NewGuid():N}"[..24],
            DataType = dataType,
            ResourceTypeKeys = new List<string> { "space" },
        };
        var response = await _client.PostAsJsonAsync("/api/criteria", create);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CriterionInfo>())!;
    }

    private async Task<Guid> CreateTestGroupAsync()
    {
        var request = new CreateResourceGroupRequest
        {
            ResourceTypeKey = "space",
            Name = $"CapTest {Guid.NewGuid():N}"[..20],
            Description = "Group for capability tests"
        };

        var response = await _client.PostAsJsonAsync("/api/resource-groups", request);
        response.EnsureSuccessStatusCode();

        var group = await response.Content.ReadFromJsonAsync<ResourceGroupInfo>();
        return group!.Id;
    }

    #region GET /api/resource-groups/{groupId}/capabilities

    [Fact]
    public async Task GetCapabilities_NoAuth_Returns401()
    {
        var groupId = Guid.NewGuid();

        var response = await _unauthenticatedClient.GetAsync($"/api/resource-groups/{groupId}/capabilities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCapabilities_ValidGroup_Returns200()
    {
        var groupId = await CreateTestGroupAsync();

        var response = await _client.GetAsync($"/api/resource-groups/{groupId}/capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var capabilities = JsonSerializer.Deserialize<List<JsonElement>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(capabilities);
    }

    [Fact]
    public async Task GetCapabilities_EmptyGroup_ReturnsEmptyList()
    {
        var groupId = await CreateTestGroupAsync();

        var response = await _client.GetAsync($"/api/resource-groups/{groupId}/capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("[]", body);
    }

    #endregion

    #region POST /api/resource-groups/{groupId}/capabilities

    [Fact]
    public async Task AddCapability_NoAuth_Returns401()
    {
        var groupId = Guid.NewGuid();
        var request = new AddGroupCapabilityRequest(
            CriterionId: Guid.NewGuid(),
            Value: 42);

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            $"/api/resource-groups/{groupId}/capabilities", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddCapability_ValidData_Returns201()
    {
        var groupId = await CreateTestGroupAsync();
        var criterion = await CreateSpaceCriterionAsync(CriterionDataType.Number);

        var request = new AddGroupCapabilityRequest(
            CriterionId: criterion.Id,
            Value: 42);

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-groups/{groupId}/capabilities", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GroupCapabilityInfo>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal(groupId, body.GroupId);
        Assert.Equal(criterion.Id, body.CriterionId);
    }

    [Fact]
    public async Task AddCapability_CriterionNotApplicableToGroupType_Returns400()
    {
        // The group is space-typed (see CreateTestGroupAsync). A criterion applicable only to
        // people must not be assignable to it — mirrors the resource-level applicability guard so
        // a group can't carry a capability its resource type isn't marked applicable to.
        var groupId = await CreateTestGroupAsync();

        var createCriterion = new CreateCriterionRequest
        {
            Name = $"grp_notapp_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "person" },
        };
        var createResponse = await _client.PostAsJsonAsync("/api/criteria", createCriterion);
        createResponse.EnsureSuccessStatusCode();
        var criterion = (await createResponse.Content.ReadFromJsonAsync<CriterionInfo>())!;

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-groups/{groupId}/capabilities",
            new AddGroupCapabilityRequest(criterion.Id, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddCapability_ThenGetReturnsIt()
    {
        var groupId = await CreateTestGroupAsync();
        var criterion = await CreateSpaceCriterionAsync(CriterionDataType.Boolean);

        var addRequest = new AddGroupCapabilityRequest(
            CriterionId: criterion.Id,
            Value: true);

        var addResponse = await _client.PostAsJsonAsync(
            $"/api/resource-groups/{groupId}/capabilities", addRequest);
        addResponse.EnsureSuccessStatusCode();

        var listResponse = await _client.GetAsync($"/api/resource-groups/{groupId}/capabilities");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var body = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(criterion.Id.ToString(), body);
    }

    #endregion

    #region DELETE /api/resource-groups/{groupId}/capabilities/{capabilityId}

    [Fact]
    public async Task DeleteCapability_NoAuth_Returns401()
    {
        var response = await _unauthenticatedClient.DeleteAsync(
            $"/api/resource-groups/{Guid.NewGuid()}/capabilities/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCapability_NonExistent_Returns404()
    {
        var groupId = await CreateTestGroupAsync();

        var response = await _client.DeleteAsync(
            $"/api/resource-groups/{groupId}/capabilities/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCapability_Existing_Returns204()
    {
        var groupId = await CreateTestGroupAsync();
        var criterion = await CreateSpaceCriterionAsync(CriterionDataType.Number);

        // Create capability
        var addRequest = new AddGroupCapabilityRequest(
            CriterionId: criterion.Id,
            Value: 99);

        var addResponse = await _client.PostAsJsonAsync(
            $"/api/resource-groups/{groupId}/capabilities", addRequest);
        addResponse.EnsureSuccessStatusCode();

        var created = await addResponse.Content.ReadFromJsonAsync<GroupCapabilityInfo>();
        Assert.NotNull(created);

        // Delete it
        var deleteResponse = await _client.DeleteAsync(
            $"/api/resource-groups/{groupId}/capabilities/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteCapability_ThenGetReturnsEmpty()
    {
        var groupId = await CreateTestGroupAsync();
        var criterion = await CreateSpaceCriterionAsync(CriterionDataType.String);

        // Create
        var addResponse = await _client.PostAsJsonAsync(
            $"/api/resource-groups/{groupId}/capabilities",
            new AddGroupCapabilityRequest(criterion.Id, "test-value"));
        addResponse.EnsureSuccessStatusCode();

        var created = await addResponse.Content.ReadFromJsonAsync<GroupCapabilityInfo>();

        // Delete
        await _client.DeleteAsync($"/api/resource-groups/{groupId}/capabilities/{created!.Id}");

        // Verify gone
        var listResponse = await _client.GetAsync($"/api/resource-groups/{groupId}/capabilities");
        var body = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.Id.ToString(), body);
    }

    #endregion

    #region Route existence

    [Fact]
    public async Task Capabilities_RouteExists_DoesNotReturn404()
    {
        var groupId = Guid.NewGuid();

        var response = await _unauthenticatedClient.GetAsync(
            $"/api/resource-groups/{groupId}/capabilities");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}
