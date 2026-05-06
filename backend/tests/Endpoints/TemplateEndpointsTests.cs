using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class TemplateEndpointsTests
{
    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _client;
    private readonly string _testTenant = TestConstants.TenantSlug;

    public TemplateEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Tenant-Slug", _testTenant);
    }

    private async Task<Guid> CreateTestCriterionAsync()
    {
        // Create a test criterion for template items
        using var conn = new NpgsqlConnection($"Host=localhost;Port={_fixture.DatabasePort};Database=tenant_{_testTenant};Username=postgres;Password=postgres");
        await conn.OpenAsync();

        var criterionId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO criteria (id, name, description, data_type, created_at, updated_at)
            VALUES (@id, @name, @description, @dataType, @now, @now)
            ON CONFLICT (id) DO NOTHING", conn);

        cmd.Parameters.AddWithValue("id", criterionId);
        cmd.Parameters.AddWithValue("name", "endpoint_test_criterion");
        cmd.Parameters.AddWithValue("description", "Endpoint Test Criterion");
        cmd.Parameters.AddWithValue("dataType", "String");
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync();
        return criterionId;
    }

    private async Task<string> GetAuthTokenAsync()
    {
        // Create a test user directly in database (legacy /api/auth/register no longer exists)
        var email = $"templatetest_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(email, "Template Test User", TestConstants.TenantSlug, "viewer", active: true);

        var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // Test tenant

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "Template Test User",
            TenantId = tenantId.ToString(),
            TenantSlug = TestConstants.TenantSlug,
            IsTenantAdmin = false,
            Role = "user"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(tokenData);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    private async Task CleanupTestDataAsync()
    {
        // Clean up test data
        using var conn = new NpgsqlConnection($"Host=localhost;Port={_fixture.DatabasePort};Database=tenant_{_testTenant};Username=postgres;Password=postgres");
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM template_items WHERE template_id IN (
                SELECT id FROM templates WHERE name LIKE 'Endpoint Test%'
            );
            DELETE FROM templates WHERE name LIKE 'Endpoint Test%';
            DELETE FROM criteria WHERE name = 'endpoint_test_criterion';", conn);

        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetTemplates_WithValidEntityType_ShouldReturnTemplates()
    {
        // Arrange
        await CleanupTestDataAsync();
        var token = await GetAuthTokenAsync();
        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test Request Template",
            Description = "Test template for endpoints",
            EntityType = "request",
            DurationValue = 60,
            DurationUnit = "minutes"
        };

        var createRequestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/templates")
        {
            Content = JsonContent.Create(createRequest)
        };
        createRequestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var createResponse = await _client.SendAsync(createRequestMessage);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Act
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/templates?entityType=request");
        getRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(getRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var templates = await response.Content.ReadFromJsonAsync<List<Template>>();
        Assert.NotNull(templates);
        Assert.Contains(templates, t => t.Name == createRequest.Name);

        // Cleanup
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task GetTemplates_WithoutEntityType_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/templates");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("entityType query parameter is required", content);
    }

    [Fact]
    public async Task GetTemplates_WithInvalidEntityType_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/templates?entityType=invalid");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid entity type: invalid", content);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("space")]
    [InlineData("group")]
    public async Task GetTemplates_WithValidEntityTypes_ShouldReturnOK(string entityType)
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/templates?entityType={entityType}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTemplateById_WithExistingTemplate_ShouldReturnTemplate()
    {
        // Arrange
        await CleanupTestDataAsync();
        var token = await GetAuthTokenAsync();
        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test Single Template",
            EntityType = "space"
        };

        var createRequestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/templates")
        {
            Content = JsonContent.Create(createRequest)
        };
        createRequestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var createResponse = await _client.SendAsync(createRequestMessage);
        var createdTemplate = await createResponse.Content.ReadFromJsonAsync<Template>();

        // Act
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/templates/{createdTemplate!.Id}");
        getRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(getRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var template = await response.Content.ReadFromJsonAsync<Template>();
        Assert.NotNull(template);
        Assert.Equal(createdTemplate.Id, template.Id);
        Assert.Equal(createRequest.Name, template.Name);

        // Cleanup
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task GetTemplateById_WithNonExistentTemplate_ShouldReturnNotFound()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/templates/{Guid.NewGuid()}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTemplate_WithValidData_ShouldCreateAndReturnTemplate()
    {
        // Arrange
        await CleanupTestDataAsync();
        var token = await GetAuthTokenAsync();
        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test New Template",
            Description = "New test template",
            EntityType = "request",
            DurationValue = 90,
            DurationUnit = "minutes",
            FixedStart = true,
            FixedEnd = false,
            FixedDuration = true
        };

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/templates")
        {
            Content = JsonContent.Create(createRequest)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("/api/templates/", response.Headers.Location?.ToString());

        var createdTemplate = await response.Content.ReadFromJsonAsync<Template>();
        Assert.NotNull(createdTemplate);
        Assert.NotEqual(Guid.Empty, createdTemplate.Id);
        Assert.Equal(createRequest.Name, createdTemplate.Name);
        Assert.Equal(createRequest.Description, createdTemplate.Description);
        Assert.Equal(createRequest.EntityType, createdTemplate.EntityType);
        Assert.Equal(createRequest.DurationValue, createdTemplate.DurationValue);
        Assert.Equal(createRequest.DurationUnit, createdTemplate.DurationUnit);
        Assert.Equal(createRequest.FixedStart, createdTemplate.FixedStart);
        Assert.Equal(createRequest.FixedEnd, createdTemplate.FixedEnd);
        Assert.Equal(createRequest.FixedDuration, createdTemplate.FixedDuration);

        // Cleanup
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task UpdateTemplate_WithExistingTemplate_ShouldReturnUpdatedTemplate()
    {
        // Arrange
        await CleanupTestDataAsync();

        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test Update Template",
            Description = "Original description",
            EntityType = "request",
            DurationValue = 30,
            DurationUnit = "minutes"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest);
        var createdTemplate = await createResponse.Content.ReadFromJsonAsync<Template>();

        // Act
        var updateRequest = new UpdateTemplateRequest
        {
            Name = "Endpoint Test Updated Template",
            Description = "Updated description",
            EntityType = "request",
            DurationValue = 60,
            DurationUnit = "minutes",
            FixedStart = true,
            FixedEnd = false,
            FixedDuration = true
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/templates/{createdTemplate!.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedTemplate = await updateResponse.Content.ReadFromJsonAsync<Template>();
        Assert.NotNull(updatedTemplate);
        Assert.Equal("Endpoint Test Updated Template", updatedTemplate.Name);
        Assert.Equal("Updated description", updatedTemplate.Description);
        Assert.Equal(60, updatedTemplate.DurationValue);
        Assert.True(updatedTemplate.FixedStart);

        // Cleanup
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task UpdateTemplate_WithNonExistentTemplate_ShouldReturnNotFound()
    {
        // Arrange
        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var updateRequest = new UpdateTemplateRequest
        {
            Name = "Non-existent Template",
            EntityType = "request"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/templates/{Guid.NewGuid()}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTemplate_WithExistingTemplate_ShouldReturnNoContent()
    {
        // Arrange
        await CleanupTestDataAsync();

        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test Delete Template",
            EntityType = "group"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest);
        var createdTemplate = await createResponse.Content.ReadFromJsonAsync<Template>();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/templates/{createdTemplate!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify template is actually deleted
        var getResponse = await _client.GetAsync($"/api/templates/{createdTemplate.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // Cleanup
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task DeleteTemplate_WithNonExistentTemplate_ShouldReturnNotFound()
    {
        // Arrange
        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        // Act
        var response = await _client.DeleteAsync($"/api/templates/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTemplateItems_WithExistingTemplate_ShouldReturnItems()
    {
        // Arrange
        await CleanupTestDataAsync();

        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var criterionId = await CreateTestCriterionAsync();

        // Create template
        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test Items Template",
            EntityType = "request"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest);
        var template = await createResponse.Content.ReadFromJsonAsync<Template>();

        // Act
        var response = await _client.GetAsync($"/api/templates/{template!.Id}/items");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<TemplateItem>>();
        Assert.NotNull(items);
        // Initially empty
        Assert.Empty(items);

        // Cleanup
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task GetTemplateItems_WithNonExistentTemplate_ShouldReturnNotFound()
    {
        // Arrange
        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        // Act
        var response = await _client.GetAsync($"/api/templates/{Guid.NewGuid()}/items");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddTemplateItem_WithValidData_ShouldCreateAndReturnItem()
    {
        // Arrange
        await CleanupTestDataAsync();

        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var criterionId = await CreateTestCriterionAsync();

        // Create template
        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test Add Item Template",
            EntityType = "request"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest);
        var template = await createResponse.Content.ReadFromJsonAsync<Template>();

        var itemRequest = new CreateTemplateItemRequest
        {
            CriterionId = criterionId,
            Value = "{\"test\": \"value\"}"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/templates/{template!.Id}/items", itemRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains($"/api/templates/{template.Id}/items/", response.Headers.Location?.ToString());

        var createdItem = await response.Content.ReadFromJsonAsync<TemplateItem>();
        Assert.NotNull(createdItem);
        Assert.NotEqual(Guid.Empty, createdItem.Id);
        Assert.Equal(template.Id, createdItem.TemplateId);
        Assert.Equal(criterionId, createdItem.CriterionId);
        Assert.Equal(itemRequest.Value, createdItem.Value);

        // Verify item can be retrieved
        var getResponse = await _client.GetAsync($"/api/templates/{template.Id}/items");
        var items = await getResponse.Content.ReadFromJsonAsync<List<TemplateItem>>();
        Assert.NotNull(items);
        Assert.Single(items);

        // Cleanup
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task DeleteTemplateItem_WithExistingItem_ShouldReturnNoContent()
    {
        // Arrange
        await CleanupTestDataAsync();

        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var criterionId = await CreateTestCriterionAsync();

        // Create template
        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test Delete Item Template",
            EntityType = "request"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest);
        var template = await createResponse.Content.ReadFromJsonAsync<Template>();

        // Create item
        var itemRequest = new CreateTemplateItemRequest
        {
            CriterionId = criterionId,
            Value = "{\"test\": \"value\"}"
        };
        var itemResponse = await _client.PostAsJsonAsync($"/api/templates/{template!.Id}/items", itemRequest);
        var item = await itemResponse.Content.ReadFromJsonAsync<TemplateItem>();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/templates/{template.Id}/items/{item!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify item is actually deleted
        var getResponse = await _client.GetAsync($"/api/templates/{template.Id}/items");
        var items = await getResponse.Content.ReadFromJsonAsync<List<TemplateItem>>();
        Assert.NotNull(items);
        Assert.Empty(items);

        // Cleanup
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task DeleteTemplateItem_WithNonExistentItem_ShouldReturnNotFound()
    {
        // Arrange
        await CleanupTestDataAsync();

        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test Delete Nonexistent Item Template",
            EntityType = "request"
        };
        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest);
        var template = await createResponse.Content.ReadFromJsonAsync<Template>();

        // Act
        var response = await _client.DeleteAsync($"/api/templates/{template!.Id}/items/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Cleanup
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task TemplateWorkflow_EndToEnd_ShouldWorkCorrectly()
    {
        // This test validates the complete template workflow
        await CleanupTestDataAsync();

        // Arrange authentication
        var authToken = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        var criterionId = await CreateTestCriterionAsync();

        // Step 1: Create template
        var createRequest = new CreateTemplateRequest
        {
            Name = "Endpoint Test E2E Template",
            Description = "End-to-end test template",
            EntityType = "request",
            DurationValue = 120,
            DurationUnit = "minutes"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var template = await createResponse.Content.ReadFromJsonAsync<Template>();

        // Step 2: Get template by ID
        var getResponse = await _client.GetAsync($"/api/templates/{template!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // Step 3: Add template item
        var itemRequest = new CreateTemplateItemRequest
        {
            CriterionId = criterionId,
            Value = "{\"category\": \"important\"}"
        };

        var itemResponse = await _client.PostAsJsonAsync($"/api/templates/{template.Id}/items", itemRequest);
        Assert.Equal(HttpStatusCode.Created, itemResponse.StatusCode);
        var item = await itemResponse.Content.ReadFromJsonAsync<TemplateItem>();

        // Step 4: Get template items
        var itemsResponse = await _client.GetAsync($"/api/templates/{template.Id}/items");
        Assert.Equal(HttpStatusCode.OK, itemsResponse.StatusCode);
        var items = await itemsResponse.Content.ReadFromJsonAsync<List<TemplateItem>>();
        Assert.NotNull(items);
        Assert.Single(items);

        // Step 5: Get all templates for entity type
        var allTemplatesResponse = await _client.GetAsync("/api/templates?entityType=request");
        Assert.Equal(HttpStatusCode.OK, allTemplatesResponse.StatusCode);
        var allTemplates = await allTemplatesResponse.Content.ReadFromJsonAsync<List<Template>>();
        Assert.Contains(allTemplates!, t => t.Id == template.Id);

        // Step 6: Delete template item
        var deleteItemResponse = await _client.DeleteAsync($"/api/templates/{template.Id}/items/{item!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteItemResponse.StatusCode);

        // Step 7: Delete template
        var deleteTemplateResponse = await _client.DeleteAsync($"/api/templates/{template.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteTemplateResponse.StatusCode);

        // Step 8: Verify template is gone
        var finalGetResponse = await _client.GetAsync($"/api/templates/{template.Id}");
        Assert.Equal(HttpStatusCode.NotFound, finalGetResponse.StatusCode);

        // Cleanup
        await CleanupTestDataAsync();
    }
}
