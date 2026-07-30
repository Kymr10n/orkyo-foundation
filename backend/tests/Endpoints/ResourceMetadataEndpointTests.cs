using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Custom field values on resources: validation against the type's field definitions, and
/// proof that a user-defined type flows through the shared resource machinery (criteria).
/// </summary>
[Collection("Database collection")]
public class ResourceMetadataEndpointTests
{
    private readonly HttpClient _client;

    public ResourceMetadataEndpointTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    /// <summary>A "car" type with a required number field (mileage) and a select field (fuel).</summary>
    private async Task<ResourceTypeInfo> CreateCarTypeAsync()
    {
        var type = (await (await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = $"car_{Guid.NewGuid():N}",
            DisplayName = "Car",
        })).Content.ReadFromJsonAsync<ResourceTypeInfo>())!;

        await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/fields", new CreateResourceTypeFieldRequest
        {
            Key = "mileage",
            Label = "Mileage",
            DataType = "number",
            IsRequired = true,
            Validation = Json("""{"min":0,"max":1000000}"""),
            SortOrder = 1,
        });

        await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/fields", new CreateResourceTypeFieldRequest
        {
            Key = "fuel",
            Label = "Fuel",
            DataType = "select",
            Options = Json("""{"values":["petrol","diesel","electric"]}"""),
            SortOrder = 2,
        });

        return type;
    }

    private Task<HttpResponseMessage> CreateCarAsync(string typeKey, string name, object? metadata) =>
        _client.PostAsJsonAsync("/api/resources", new
        {
            resourceTypeKey = typeKey,
            name,
            allocationMode = "Exclusive",
            metadata,
        });

    [Fact]
    public async Task CreateResource_StoresAndReturnsMetadata()
    {
        var type = await CreateCarTypeAsync();

        var response = await CreateCarAsync(type.Key, "Fleet car",
            new { mileage = 42000, fuel = "diesel" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.NotNull(created!.Metadata);
        Assert.Equal(42000, created.Metadata!.Value.GetProperty("mileage").GetInt32());

        // Round-trips through a fresh read, not just the create response.
        var fetched = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{created.Id}");
        Assert.Equal("diesel", fetched!.Metadata!.Value.GetProperty("fuel").GetString());
    }

    [Fact]
    public async Task CreateResource_RejectsUnknownField()
    {
        var type = await CreateCarTypeAsync();

        var response = await CreateCarAsync(type.Key, "Bad car",
            new { mileage = 1000, colour = "red" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_RejectsMissingRequiredField()
    {
        var type = await CreateCarTypeAsync();

        var response = await CreateCarAsync(type.Key, "No mileage", new { fuel = "petrol" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_RejectsWrongValueKind()
    {
        var type = await CreateCarTypeAsync();

        var response = await CreateCarAsync(type.Key, "Text mileage", new { mileage = "lots" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_RejectsNumberOutOfRange()
    {
        var type = await CreateCarTypeAsync();

        var response = await CreateCarAsync(type.Key, "Negative mileage", new { mileage = -5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_RejectsValueOutsideSelectOptions()
    {
        var type = await CreateCarTypeAsync();

        var response = await CreateCarAsync(type.Key, "Steam car",
            new { mileage = 10, fuel = "steam" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_RejectsInactiveType()
    {
        var type = await CreateCarTypeAsync();
        await _client.PutAsJsonAsync($"/api/resource-types/{type.Id}",
            new UpdateResourceTypeRequest { IsActive = false });

        var response = await CreateCarAsync(type.Key, "Retired type car", new { mileage = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateResource_ReplacesMetadataDocument()
    {
        var type = await CreateCarTypeAsync();
        var created = await (await CreateCarAsync(type.Key, "Updatable car",
            new { mileage = 100, fuel = "petrol" })).Content.ReadFromJsonAsync<ResourceInfo>();

        var response = await _client.PutAsJsonAsync($"/api/resources/{created!.Id}",
            new { metadata = new { mileage = 200 } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{created.Id}");
        Assert.Equal(200, updated!.Metadata!.Value.GetProperty("mileage").GetInt32());
        // Replacement, not merge: the previously-set optional field is gone.
        Assert.False(updated.Metadata!.Value.TryGetProperty("fuel", out _));
    }

    [Fact]
    public async Task UpdateResource_WithoutMetadata_LeavesDocumentUntouched()
    {
        var type = await CreateCarTypeAsync();
        var created = await (await CreateCarAsync(type.Key, "Renamed car",
            new { mileage = 500 })).Content.ReadFromJsonAsync<ResourceInfo>();

        var response = await _client.PutAsJsonAsync($"/api/resources/{created!.Id}",
            new { name = "Renamed car II" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{created.Id}");
        Assert.Equal("Renamed car II", updated!.Name);
        Assert.Equal(500, updated.Metadata!.Value.GetProperty("mileage").GetInt32());
    }

    [Fact]
    public async Task UpdateResource_RejectsInvalidMetadata()
    {
        var type = await CreateCarTypeAsync();
        var created = await (await CreateCarAsync(type.Key, "Guarded car",
            new { mileage = 100 })).Content.ReadFromJsonAsync<ResourceInfo>();

        var response = await _client.PutAsJsonAsync($"/api/resources/{created!.Id}",
            new { metadata = new { mileage = "many" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_OnTypeWithoutFields_AcceptsNoMetadata()
    {
        var type = (await (await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = $"camera_{Guid.NewGuid():N}",
            DisplayName = "Camera",
        })).Content.ReadFromJsonAsync<ResourceTypeInfo>())!;

        var response = await CreateCarAsync(type.Key, "Camera A", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CustomType_AcceptsCriteria()
    {
        // Regression guard for retiring the hard-coded ResourceTypeKeys.IsKnown gate: a criterion
        // must be assignable to a user-defined type, and listable by that type key.
        var type = await CreateCarTypeAsync();

        var create = await _client.PostAsJsonAsync("/api/criteria", new
        {
            name = $"car_criterion_{Guid.NewGuid():N}"[..30],
            dataType = "Boolean",
            resourceTypeKeys = new[] { type.Key },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var listed = await _client.GetAsync($"/api/criteria?resourceType={type.Key}");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
    }

    [Fact]
    public async Task Criteria_StillRejectUnknownResourceTypeKey()
    {
        var response = await _client.GetAsync("/api/criteria?resourceType=definitely_not_a_type");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
