using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

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
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
    }

    private async Task<string> GetAuthTokenAsync()
    {
        // Create a test user directly in database (Keycloak handles real auth)
        var email = $"preftest_{Guid.NewGuid()}@example.com";
        var displayName = "Preferences Test User";

        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, displayName, TestConstants.TenantSlug, "viewer", active: true);
        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // Test tenant

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = displayName,
            TenantId = tenantId.ToString(),
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = false,
            Role = "user"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(tokenData);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public async Task GetPreferences_WhenNoPreferencesExist_ShouldReturnEmptyObject()
    {
        // Arrange
        var token = await GetAuthTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/preferences");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

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

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/preferences")
        {
            Content = JsonContent.Create(preferences)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

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
        var updateRequest = new HttpRequestMessage(HttpMethod.Put, "/api/preferences")
        {
            Content = JsonContent.Create(preferences)
        };
        updateRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act - Get preferences
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/preferences");
        getRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
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
    public async Task UpdatePreferences_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var preferences = new { spaceOrder = new[] { "space-1" } };

        // Act
        var response = await _client.PutAsJsonAsync("/api/preferences", preferences);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPreferences_WithoutAuth_ShouldReturn401()
    {
        // Act
        var response = await _client.GetAsync("/api/preferences");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdatePreferences_MultipleUpdates_ShouldOverwritePrevious()
    {
        // Arrange
        var token = await GetAuthTokenAsync();

        var firstPreferences = new { spaceOrder = new[] { "a", "b", "c" } };
        var secondPreferences = new { spaceOrder = new[] { "x", "y", "z" } };

        // Act - First update
        var firstRequest = new HttpRequestMessage(HttpMethod.Put, "/api/preferences")
        {
            Content = JsonContent.Create(firstPreferences)
        };
        firstRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(firstRequest);

        // Act - Second update
        var secondRequest = new HttpRequestMessage(HttpMethod.Put, "/api/preferences")
        {
            Content = JsonContent.Create(secondPreferences)
        };
        secondRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await _client.SendAsync(secondRequest);

        // Act - Get current preferences
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/preferences");
        getRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(getRequest);

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var spaceOrder = json.RootElement.GetProperty("spaceOrder");
        spaceOrder[0].GetString().Should().Be("x");
        spaceOrder[1].GetString().Should().Be("y");
        spaceOrder[2].GetString().Should().Be("z");
    }

    private record LoginResponse(string Token, UserResponse User);
    private record UserResponse(Guid Id, string Email, string DisplayName, bool IsTenantAdmin);
}
