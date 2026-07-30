using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// User-defined resource types and their custom field definitions. The fixture database is
/// shared across the suite, so every test mints its own type key.
/// </summary>
[Collection("Database collection")]
public class ResourceTypeFieldEndpointTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;

    public ResourceTypeFieldEndpointTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    // Guid "N" is lowercase hex, so the result satisfies the key format constraint.
    private static string UniqueKey(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private async Task<ResourceTypeInfo> CreateTypeAsync(string? key = null)
    {
        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = key ?? UniqueKey("car"),
            DisplayName = "Car",
            Description = "Fleet vehicle",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;
    }

    private async Task<ResourceTypeFieldInfo> AddFieldAsync(Guid typeId, CreateResourceTypeFieldRequest request)
    {
        var response = await _client.PostAsJsonAsync($"/api/resource-types/{typeId}/fields", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceTypeFieldInfo>())!;
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    // ── type lifecycle ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateResourceType_CreatesNonSystemActiveType()
    {
        var created = await CreateTypeAsync();

        Assert.False(created.IsSystem);
        Assert.True(created.IsActive);
        Assert.Equal("Car", created.DisplayName);
    }

    [Fact]
    public async Task CreateResourceType_RejectsDuplicateKey()
    {
        var key = UniqueKey("dup");
        await CreateTypeAsync(key);

        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = key,
            DisplayName = "Duplicate",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Car")]        // uppercase
    [InlineData("2wheeler")]   // leading digit
    [InlineData("my-type")]    // hyphen
    public async Task CreateResourceType_RejectsMalformedKey(string key)
    {
        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = key,
            DisplayName = "Bad key",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateResourceType_ChangesDisplayName()
    {
        var created = await CreateTypeAsync();

        var response = await _client.PutAsJsonAsync($"/api/resource-types/{created.Id}",
            new UpdateResourceTypeRequest { DisplayName = "Company car" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ResourceTypeInfo>();
        Assert.Equal("Company car", updated!.DisplayName);
    }

    [Fact]
    public async Task DeleteResourceType_RemovesUnusedType()
    {
        var created = await CreateTypeAsync();

        var response = await _client.DeleteAsync($"/api/resource-types/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var lookup = await _client.GetAsync($"/api/resource-types/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task DeleteResourceType_DeactivatesTypeThatStillHasResources()
    {
        var type = await CreateTypeAsync();
        var create = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = type.Key,
            Name = "Fleet car 1",
            AllocationMode = "Exclusive",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var response = await _client.DeleteAsync($"/api/resource-types/{type.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Still present, but retired.
        var after = await _client.GetFromJsonAsync<ResourceTypeInfo>($"/api/resource-types/{type.Id}");
        Assert.NotNull(after);
        Assert.False(after!.IsActive);
    }

    [Fact]
    public async Task SystemTypes_CannotBeUpdatedOrDeleted()
    {
        var all = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");
        var space = all!.First(t => t.Key == "space");

        var update = await _client.PutAsJsonAsync($"/api/resource-types/{space.Id}",
            new UpdateResourceTypeRequest { DisplayName = "Renamed" });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);

        var delete = await _client.DeleteAsync($"/api/resource-types/{space.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
    }

    [Fact]
    public async Task GetResourceTypes_FiltersByActiveState()
    {
        var type = await CreateTypeAsync();
        await _client.PutAsJsonAsync($"/api/resource-types/{type.Id}",
            new UpdateResourceTypeRequest { IsActive = false });

        var active = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types?isActive=true");
        Assert.DoesNotContain(active!, t => t.Id == type.Id);

        var inactive = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types?isActive=false");
        Assert.Contains(inactive!, t => t.Id == type.Id);
    }

    [Fact]
    public async Task ViewerCannotWriteResourceTypes()
    {
        var viewer = _fixture.CreateClientWithRole("viewer");

        var response = await viewer.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("viewer"),
            DisplayName = "Nope",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── field definitions ─────────────────────────────────────────────────────

    [Fact]
    public async Task AddField_CreatesDefinition()
    {
        var type = await CreateTypeAsync();

        var field = await AddFieldAsync(type.Id, new CreateResourceTypeFieldRequest
        {
            Key = "mileage",
            Label = "Mileage",
            DataType = "number",
            IsRequired = true,
            Validation = Json("""{"min":0}"""),
        });

        Assert.Equal("mileage", field.Key);
        Assert.True(field.IsRequired);
        Assert.True(field.IsActive);
    }

    [Fact]
    public async Task AddField_RejectsDuplicateKeyOnSameType()
    {
        var type = await CreateTypeAsync();
        await AddFieldAsync(type.Id, new CreateResourceTypeFieldRequest
        {
            Key = "mileage", Label = "Mileage", DataType = "number",
        });

        var response = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/fields",
            new CreateResourceTypeFieldRequest { Key = "mileage", Label = "Again", DataType = "number" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddField_RejectsSelectWithoutOptions()
    {
        var type = await CreateTypeAsync();

        var response = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/fields",
            new CreateResourceTypeFieldRequest { Key = "fuel", Label = "Fuel", DataType = "select" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddField_RejectsUnknownDataType()
    {
        var type = await CreateTypeAsync();

        var response = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/fields",
            new CreateResourceTypeFieldRequest { Key = "weird", Label = "Weird", DataType = "money" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SystemTypes_AcceptFieldDefinitions()
    {
        var all = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");
        var tool = all!.First(t => t.Key == "tool");

        var key = $"purchased_{Guid.NewGuid():N}"[..20];
        var field = await AddFieldAsync(tool.Id, new CreateResourceTypeFieldRequest
        {
            Key = key, Label = "Purchase date", DataType = "date",
        });

        Assert.Equal(key, field.Key);
    }

    [Fact]
    public async Task UpdateField_ChangesLabelAndRequiredFlag()
    {
        var type = await CreateTypeAsync();
        var field = await AddFieldAsync(type.Id, new CreateResourceTypeFieldRequest
        {
            Key = "mileage", Label = "Mileage", DataType = "number",
        });

        var response = await _client.PutAsJsonAsync($"/api/resource-types/{type.Id}/fields/{field.Id}",
            new UpdateResourceTypeFieldRequest { Label = "Odometer", IsRequired = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ResourceTypeFieldInfo>();
        Assert.Equal("Odometer", updated!.Label);
        Assert.True(updated.IsRequired);
    }

    [Fact]
    public async Task UpdateField_Returns404_WhenFieldBelongsToAnotherType()
    {
        var typeA = await CreateTypeAsync();
        var typeB = await CreateTypeAsync();
        var field = await AddFieldAsync(typeA.Id, new CreateResourceTypeFieldRequest
        {
            Key = "mileage", Label = "Mileage", DataType = "number",
        });

        var response = await _client.PutAsJsonAsync($"/api/resource-types/{typeB.Id}/fields/{field.Id}",
            new UpdateResourceTypeFieldRequest { Label = "Hijacked" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateField_HidesItFromDefaultListing()
    {
        var type = await CreateTypeAsync();
        var field = await AddFieldAsync(type.Id, new CreateResourceTypeFieldRequest
        {
            Key = "mileage", Label = "Mileage", DataType = "number",
        });

        var response = await _client.DeleteAsync($"/api/resource-types/{type.Id}/fields/{field.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var active = await _client.GetFromJsonAsync<List<ResourceTypeFieldInfo>>(
            $"/api/resource-types/{type.Id}/fields");
        Assert.DoesNotContain(active!, f => f.Id == field.Id);

        var all = await _client.GetFromJsonAsync<List<ResourceTypeFieldInfo>>(
            $"/api/resource-types/{type.Id}/fields?includeInactive=true");
        Assert.Contains(all!, f => f.Id == field.Id && !f.IsActive);
    }

    [Fact]
    public async Task GetFields_OrdersBySortOrder()
    {
        var type = await CreateTypeAsync();
        await AddFieldAsync(type.Id, new CreateResourceTypeFieldRequest
        {
            Key = "second", Label = "Second", DataType = "text", SortOrder = 2,
        });
        await AddFieldAsync(type.Id, new CreateResourceTypeFieldRequest
        {
            Key = "first", Label = "First", DataType = "text", SortOrder = 1,
        });

        var fields = await _client.GetFromJsonAsync<List<ResourceTypeFieldInfo>>(
            $"/api/resource-types/{type.Id}/fields");

        Assert.Equal(["first", "second"], fields!.Select(f => f.Key));
    }
}
