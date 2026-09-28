using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Constants;
using Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, _testTenant);
    }

    private async Task<Guid> CreateTestCriterionAsync()
    {
        // Create a test criterion for template items
        using var conn = new NpgsqlConnection(_fixture.TenantConnectionString);
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

    // The scope tests write through the API as an editor, like the assignment and request
    // suites do, rather than through the raw-SQL helper above.
    private HttpClient Authorized => _authorized ??= _fixture.CreateAuthorizedClient();
    private HttpClient? _authorized;

    private async Task<Guid> CreateScopedCriterionAsync(params string[] resourceTypeKeys)
    {
        var response = await Authorized.PostAsJsonAsync("/api/criteria", new
        {
            name = $"c_{Guid.NewGuid():N}"[..20],
            dataType = "Boolean",
            resourceTypeKeys,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CriterionInfo>())!.Id;
    }

    private async Task<Template> CreateRequestTemplateAsync(params string[] targetResourceTypeKeys)
    {
        var response = await Authorized.PostAsJsonAsync("/api/templates", new CreateTemplateRequest
        {
            Name = $"Scoped {Guid.NewGuid():N}"[..20],
            EntityType = "request",
            DurationValue = 1,
            DurationUnit = "hours",
            TargetResourceTypeKeys = targetResourceTypeKeys,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Template>())!;
    }

    // ── Item scope: a criterion must apply to a type the template's requests can hold ──

    [Fact]
    public async Task AddItem_RejectsACriterionThatAppliesToNoTargetType()
    {
        var template = await CreateRequestTemplateAsync(ResourceTypeKeys.Space);
        var toolCriterion = await CreateScopedCriterionAsync(ResourceTypeKeys.Tool);

        var response = await Authorized.PostAsJsonAsync($"/api/templates/{template.Id}/items",
            new CreateTemplateItemRequest { CriterionId = toolCriterion, Value = "true" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var items = await Authorized.GetFromJsonAsync<List<TemplateItem>>($"/api/templates/{template.Id}/items");
        Assert.Empty(items!);
    }

    [Fact]
    public async Task AddItem_AcceptsAPersonSkillOnATemplateThatTargetsNoPeople()
    {
        // People are staffed on the request rather than named under Needs, so their skills
        // are always a valid item.
        var template = await CreateRequestTemplateAsync(ResourceTypeKeys.Space);
        var personCriterion = await CreateScopedCriterionAsync(ResourceTypeKeys.Person);

        var response = await Authorized.PostAsJsonAsync($"/api/templates/{template.Id}/items",
            new CreateTemplateItemRequest { CriterionId = personCriterion, Value = "true" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DeleteItem_ThroughAnotherTemplatesRoute_Returns404AndKeepsTheItem()
    {
        var templateA = await CreateRequestTemplateAsync(ResourceTypeKeys.Space);
        var templateB = await CreateRequestTemplateAsync(ResourceTypeKeys.Space);
        var criterion = await CreateScopedCriterionAsync(ResourceTypeKeys.Space);
        var added = await Authorized.PostAsJsonAsync($"/api/templates/{templateB.Id}/items",
            new CreateTemplateItemRequest { CriterionId = criterion, Value = "true" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var itemOfB = (await added.Content.ReadFromJsonAsync<TemplateItem>())!;

        var response = await Authorized.DeleteAsync($"/api/templates/{templateA.Id}/items/{itemOfB.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var items = await Authorized.GetFromJsonAsync<List<TemplateItem>>($"/api/templates/{templateB.Id}/items");
        Assert.Contains(items!, i => i.Id == itemOfB.Id);
    }

    [Fact]
    public async Task UpdateTemplate_RejectsNarrowingTheTargetsAwayFromAnItem()
    {
        var template = await CreateRequestTemplateAsync(ResourceTypeKeys.Space, ResourceTypeKeys.Tool);
        var toolCriterion = await CreateScopedCriterionAsync(ResourceTypeKeys.Tool);
        var added = await Authorized.PostAsJsonAsync($"/api/templates/{template.Id}/items",
            new CreateTemplateItemRequest { CriterionId = toolCriterion, Value = "true" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var response = await Authorized.PutAsJsonAsync($"/api/templates/{template.Id}", new UpdateTemplateRequest
        {
            Name = template.Name,
            EntityType = "request",
            DurationValue = 1,
            DurationUnit = "hours",
            TargetResourceTypeKeys = [ResourceTypeKeys.Space],
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await Authorized.GetFromJsonAsync<Template>($"/api/templates/{template.Id}");
        Assert.Equal([ResourceTypeKeys.Space, ResourceTypeKeys.Tool], after!.TargetResourceTypeKeys.Order());
    }

    private static Task<string> GetAuthTokenAsync() => DatabaseFixture.CreateMemberTokenAsync("admin");

    private async Task CleanupTestDataAsync()
    {
        // Clean up test data
        using var conn = new NpgsqlConnection(_fixture.TenantConnectionString);
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

        var createRequestMessage = TestHelpers.AuthRequest(HttpMethod.Post, "/api/templates", token, createRequest);

        var createResponse = await _client.SendAsync(createRequestMessage);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Act
        var getRequest = TestHelpers.AuthRequest(HttpMethod.Get, "/api/templates?entityType=request", token);
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
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/templates", token);

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
        var request = TestHelpers.AuthRequest(HttpMethod.Get, "/api/templates?entityType=invalid", token);

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
        var request = TestHelpers.AuthRequest(HttpMethod.Get, $"/api/templates?entityType={entityType}", token);

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

        var createRequestMessage = TestHelpers.AuthRequest(HttpMethod.Post, "/api/templates", token, createRequest);

        var createResponse = await _client.SendAsync(createRequestMessage);
        var createdTemplate = await createResponse.Content.ReadFromJsonAsync<Template>();

        // Act
        var getRequest = TestHelpers.AuthRequest(HttpMethod.Get, $"/api/templates/{createdTemplate!.Id}", token);
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
        var request = TestHelpers.AuthRequest(HttpMethod.Get, $"/api/templates/{Guid.NewGuid()}", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTemplate_DescriptionWithinTheValidatorLimit_Creates()
    {
        // The validator allows 1000 characters; a stale repository guard once refused past 255.
        var description = new string('d', 600);
        var response = await Authorized.PostAsJsonAsync("/api/templates", new CreateTemplateRequest
        {
            Name = $"S30 {Guid.NewGuid():N}",
            Description = description,
            EntityType = "request",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Template>();
        Assert.Equal(description, created!.Description);
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
        var request = TestHelpers.AuthRequest(HttpMethod.Post, "/api/templates", token, createRequest);
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
    public async Task DeleteTemplate_Twice_SecondReturnsNotFound()
    {
        var template = await CreateRequestTemplateAsync();

        var first = await Authorized.DeleteAsync($"/api/templates/{template.Id}");
        var second = await Authorized.DeleteAsync($"/api/templates/{template.Id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task AddTemplateItem_WithNonExistentTemplate_ShouldReturnNotFound()
    {
        var response = await Authorized.PostAsJsonAsync($"/api/templates/{Guid.NewGuid()}/items",
            new CreateTemplateItemRequest { CriterionId = Guid.NewGuid(), Value = "{\"test\": \"value\"}" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
