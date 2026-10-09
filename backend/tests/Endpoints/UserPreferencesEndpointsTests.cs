using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class UserPreferencesEndpointsTests
{
    private readonly HttpClient _client;
    private readonly FoundationWebApplicationFactory _factory;
    private const string TenantSlug = TestConstants.TenantSlug;

    public UserPreferencesEndpointsTests(DatabaseFixture databaseFixture)
    {
        _factory = databaseFixture.Factory;
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
    }

    private static Task<string> GetAuthTokenAsync() => DatabaseFixture.CreateMemberTokenAsync("admin");

    [Fact]
    public async Task GetPreferences_WhenNoPreferencesExist_ShouldReturnEmptyObject()
    {
        // Arrange
        var token = await GetAuthTokenAsync();

        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/preferences", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task UpdatePreferences_WithValidData_ShouldReturn200()
    {
        // Arrange
        var token = await GetAuthTokenAsync();

        var preferences = new
        {
            spaceOrder = new[] { "space-1", "space-2", "space-3" },
            theme = "dark"
        };

        var request = TestHelpers.AuthRequest(HttpMethod.Put, "/api/preferences", token, preferences);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Preferences updated successfully");
    }

    [Fact]
    public async Task UpdateAndGetPreferences_ShouldPersistData()
    {
        // Arrange
        var token = await GetAuthTokenAsync();

        var uniqueSpaceOrder = new[]
        {
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString()
        };
        var preferences = new
        {
            spaceOrder = uniqueSpaceOrder,
            viewMode = "grid"
        };

        // Act - Update preferences
        var updateRequest = TestHelpers.AuthRequest(HttpMethod.Put, "/api/preferences", token, preferences);
        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act - Get preferences
        var getRequest = TestHelpers.AuthRequest(HttpMethod.Get, "/api/preferences", token);
        var getResponse = await _client.SendAsync(getRequest);

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await getResponse.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Verify spaceOrder was persisted
        var spaceOrderElement = json.RootElement.GetProperty("spaceOrder");
        spaceOrderElement.GetArrayLength().Should().Be(3);
        spaceOrderElement[0].GetString().Should().Be(uniqueSpaceOrder[0]);
        spaceOrderElement[1].GetString().Should().Be(uniqueSpaceOrder[1]);
        spaceOrderElement[2].GetString().Should().Be(uniqueSpaceOrder[2]);

        // Verify viewMode was persisted
        json.RootElement.GetProperty("viewMode").GetString().Should().Be("grid");
    }

    [Fact]
    public async Task UpdatePreferences_MultipleUpdates_ShouldOverwritePrevious()
    {
        // Arrange
        var token = await GetAuthTokenAsync();

        var firstPreferences = new { spaceOrder = new[] { "a", "b", "c" } };
        var secondPreferences = new { spaceOrder = new[] { "x", "y", "z" } };

        // Act - First update
        var firstRequest = TestHelpers.AuthRequest(HttpMethod.Put, "/api/preferences", token, firstPreferences);
        await _client.SendAsync(firstRequest);

        // Act - Second update
        var secondRequest = TestHelpers.AuthRequest(HttpMethod.Put, "/api/preferences", token, secondPreferences);
        await _client.SendAsync(secondRequest);

        // Act - Get current preferences
        var getRequest = TestHelpers.AuthRequest(HttpMethod.Get, "/api/preferences", token);
        var response = await _client.SendAsync(getRequest);

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var spaceOrder = json.RootElement.GetProperty("spaceOrder");
        spaceOrder[0].GetString().Should().Be("x");
        spaceOrder[1].GetString().Should().Be("y");
        spaceOrder[2].GetString().Should().Be("z");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Preferences_RefuseASignedInUserWhoIsNotAMemberOfTheTenant(string method)
    {
        // The preferences row lives in the tenant database. Without the membership gate a
        // signed-in user could read or write their own row in any tenant whose slug they name
        // (orkyo-saas#296).
        using var outsider = _factory.CreateClient();
        outsider.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
        outsider.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.BearerTokenForRole(RoleConstants.None)}");

        var response = method == "GET"
            ? await outsider.GetAsync("/api/preferences")
            : await outsider.PutAsJsonAsync("/api/preferences", new { theme = "dark" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private record LoginResponse(string Token, UserResponse User);
    private record UserResponse(Guid Id, string Email, string DisplayName, bool IsTenantAdmin);
}
