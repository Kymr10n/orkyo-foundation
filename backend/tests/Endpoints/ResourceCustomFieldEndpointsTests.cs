using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Custom fields: the descriptive properties a tenant defines on a resource type. Two things
/// are load-bearing here and both are asserted — defining a field is Admin-only while reading
/// the definitions is not, and the definition's key and data type never change once values
/// could exist behind them.
/// </summary>
[Collection("Database collection")]
public class ResourceCustomFieldEndpointsTests
{
    private readonly HttpClient _client;

    public ResourceCustomFieldEndpointsTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private readonly DatabaseFixture _fixture;

    private static string UniqueKey(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private async Task<ResourceTypeInfo> CreateTypeAsync(bool hasGeometry = false)
    {
        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("machine"),
            DisplayName = "Machine",
            DisplayNamePlural = "Machines",
            HasGeometry = hasGeometry,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;
    }

    private async Task<ResourceCustomFieldInfo> CreateFieldAsync(
        Guid typeId, string key, string dataType = CustomFieldDataTypes.Text,
        bool isRequired = false, int sortOrder = 0)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{typeId}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = key,
                Label = key,
                DataType = dataType,
                IsRequired = isRequired,
                SortOrder = sortOrder,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;
    }

    // ── definition lifecycle ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateCustomField_ReturnsDefinitionWithDefaults()
    {
        var type = await CreateTypeAsync();

        var field = await CreateFieldAsync(type.Id, "serial_number");

        Assert.Equal("serial_number", field.Key);
        Assert.Equal(CustomFieldDataTypes.Text, field.DataType);
        Assert.False(field.IsRequired);
        Assert.True(field.IsActive);
    }

    [Fact]
    public async Task CreateCustomField_RejectsDuplicateKeyForSameType()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number");

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "serial_number",
                Label = "Serial number, again",
                DataType = CustomFieldDataTypes.Text,
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateCustomField_RejectsUnknownDataType()
    {
        var type = await CreateTypeAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "colour",
                Label = "Colour",
                DataType = "rainbow",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomFields_ReturnsThemInFormOrder()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "second", sortOrder: 2);
        await CreateFieldAsync(type.Id, "first", sortOrder: 1);

        var fields = await _client.GetFromJsonAsync<List<ResourceCustomFieldInfo>>(
            $"/api/resource-types/{type.Id}/custom-fields");

        Assert.Equal(["first", "second"], fields!.Select(f => f.Key));
    }

    [Fact]
    public async Task GetCustomFields_ReturnsNotFoundForUnknownResourceType()
    {
        var response = await _client.GetAsync($"/api/resource-types/{Guid.NewGuid()}/custom-fields");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCustomField_ChangesLabelAndRequiredness()
    {
        var type = await CreateTypeAsync();
        var field = await CreateFieldAsync(type.Id, "serial_number");

        var response = await _client.PutAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields/{field.Id}",
            new UpdateResourceCustomFieldRequest { Label = "Serial no.", IsRequired = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;
        Assert.Equal("Serial no.", updated.Label);
        Assert.True(updated.IsRequired);
        // Key and data type are not in the request shape at all, so they cannot drift.
        Assert.Equal("serial_number", updated.Key);
        Assert.Equal(CustomFieldDataTypes.Text, updated.DataType);
    }

    [Fact]
    public async Task UpdateCustomField_ReturnsNotFoundWhenFieldBelongsToAnotherType()
    {
        var owningType = await CreateTypeAsync();
        var otherType = await CreateTypeAsync();
        var field = await CreateFieldAsync(owningType.Id, "serial_number");

        var response = await _client.PutAsJsonAsync(
            $"/api/resource-types/{otherType.Id}/custom-fields/{field.Id}",
            new UpdateResourceCustomFieldRequest { Label = "Hijacked" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCustomField_StripsTheValueFromResourcesOfThatType()
    {
        var type = await CreateTypeAsync();
        var field = await CreateFieldAsync(type.Id, "serial_number");

        var created = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = type.Key,
            Name = "Lathe",
            AllocationMode = "Exclusive",
            CustomFields = new() { ["serial_number"] = JsonDocument.Parse("\"SN-1\"").RootElement },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var resource = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;

        var deleted = await _client.DeleteAsync($"/api/resource-types/{type.Id}/custom-fields/{field.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Left behind, the value would reappear under any later field reusing the key — with a
        // different data type and no definition to validate it against.
        var after = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{resource.Id}");
        Assert.Null(after!.CustomFields);
    }

    [Fact]
    public async Task CreateCustomField_ReturnsALocationThatResolves()
    {
        var type = await CreateTypeAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "serial_number",
                Label = "Serial number",
                DataType = CustomFieldDataTypes.Text,
            });

        var location = response.Headers.Location!.ToString();
        var fetched = await _client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("serial_number", (await fetched.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!.Key);
    }

    [Fact]
    public async Task GetCustomField_ReturnsNotFoundWhenItBelongsToAnotherType()
    {
        var owningType = await CreateTypeAsync();
        var otherType = await CreateTypeAsync();
        var field = await CreateFieldAsync(owningType.Id, "serial_number");

        var response = await _client.GetAsync(
            $"/api/resource-types/{otherType.Id}/custom-fields/{field.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateCustomField_ReturnsNotFoundForUnknownResourceType()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{Guid.NewGuid()}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "serial_number",
                Label = "Serial number",
                DataType = CustomFieldDataTypes.Text,
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCustomField_ReturnsNotFoundForUnknownField()
    {
        var type = await CreateTypeAsync();

        var response = await _client.DeleteAsync(
            $"/api/resource-types/{type.Id}/custom-fields/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCustomField_ReturnsNotFoundWhenFieldBelongsToAnotherType()
    {
        var owningType = await CreateTypeAsync();
        var otherType = await CreateTypeAsync();
        var field = await CreateFieldAsync(owningType.Id, "serial_number");

        var response = await _client.DeleteAsync(
            $"/api/resource-types/{otherType.Id}/custom-fields/{field.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCustomField_ChangesDescriptionAndOrder()
    {
        var type = await CreateTypeAsync();
        var field = await CreateFieldAsync(type.Id, "serial_number");

        var response = await _client.PutAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields/{field.Id}",
            new UpdateResourceCustomFieldRequest { Description = "Stamped on the frame", SortOrder = 7 });

        var updated = (await response.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;
        Assert.Equal("Stamped on the frame", updated.Description);
        Assert.Equal(7, updated.SortOrder);
    }

    [Fact]
    public async Task UpdateCustomField_WithNothingSetLeavesTheFieldAsItWas()
    {
        var type = await CreateTypeAsync();
        var field = await CreateFieldAsync(type.Id, "serial_number", isRequired: true, sortOrder: 3);

        var response = await _client.PutAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields/{field.Id}",
            new UpdateResourceCustomFieldRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;
        Assert.Equal("serial_number", updated.Label);
        Assert.True(updated.IsRequired);
        Assert.Equal(3, updated.SortOrder);
    }

    // ── definition validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Serial")]          // uppercase
    [InlineData("1serial")]         // leading digit
    [InlineData("serial-number")]   // hyphen
    [InlineData("")]                // empty
    public async Task CreateCustomField_RejectsMalformedKey(string key)
    {
        var type = await CreateTypeAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = key,
                Label = "Serial number",
                DataType = CustomFieldDataTypes.Text,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCustomField_RejectsBlankLabel()
    {
        var type = await CreateTypeAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "serial_number",
                Label = "   ",
                DataType = CustomFieldDataTypes.Text,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCustomField_RejectsBlankLabel()
    {
        var type = await CreateTypeAsync();
        var field = await CreateFieldAsync(type.Id, "serial_number");

        var response = await _client.PutAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields/{field.Id}",
            new UpdateResourceCustomFieldRequest { Label = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCustomField_RejectsOverLongDescription()
    {
        var type = await CreateTypeAsync();
        var field = await CreateFieldAsync(type.Id, "serial_number");

        var response = await _client.PutAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields/{field.Id}",
            new UpdateResourceCustomFieldRequest { Description = new string('x', 2001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── requiredness is only offered where the form can ask for it ────────────

    [Fact]
    public async Task CreateCustomField_AllowsRequiredOnADirectoryType()
    {
        // People are created through /api/resources, which carries values fine — a dialog that
        // does not ask for them is a gap in that dialog, not a reason to refuse here.
        var directory = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("contractor"),
            DisplayName = "Contractor",
            DisplayNamePlural = "Contractors",
            HasDirectoryProfile = true,
        });
        var type = (await directory.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "badge_number",
                Label = "Badge number",
                DataType = CustomFieldDataTypes.Text,
                IsRequired = true,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>The seeded space type — the one type created from a form that cannot carry values.</summary>
    private async Task<ResourceTypeInfo> SpaceTypeAsync()
    {
        var types = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");
        return types!.Single(t => t is { IsSystem: true, HasGeometry: true });
    }

    [Fact]
    public async Task CreateCustomField_AllowsRequiredOnATenantDefinedPlaceableType()
    {
        // A tenant's own placeable type is created through /api/resources like everything
        // else, and that carries values — so requiring one is safe. Only the built-in space
        // has a create form with nowhere to put them.
        var placeable = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("bay"),
            DisplayName = "Bay",
            DisplayNamePlural = "Bays",
            HasGeometry = true,
        });
        var type = (await placeable.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "serial_number",
                Label = "Serial number",
                DataType = CustomFieldDataTypes.Text,
                IsRequired = true,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateCustomField_AllowsRequiredOnAPlaceableType()
    {
        // A required field on a placeable type used to be rejected outright: the floorplan's
        // create dialog sent no custom-field document, so the only path that creates a placed
        // resource could never satisfy one. The dialog asks now, and the guard is gone.
        //
        // Deliberately on a throwaway type rather than the shared `space` one: these tests commit,
        // and a required field left on a type other suites create bare resources of would fail
        // every one of them. That cascade is exactly what the old guard's comment described.
        var placeable = await CreateTypeAsync(hasGeometry: true);

        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{placeable.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = UniqueKey("floor_finish"),
                Label = "Floor finish",
                DataType = CustomFieldDataTypes.Text,
                IsRequired = true,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCustomField_IsForbiddenForEditors()
    {
        var type = await CreateTypeAsync();
        var editorClient = _fixture.CreateClientWithRole("editor");

        var response = await editorClient.PostAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "serial_number",
                Label = "Serial number",
                DataType = CustomFieldDataTypes.Text,
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAndDeleteCustomField_AreForbiddenForEditors()
    {
        var type = await CreateTypeAsync();
        var field = await CreateFieldAsync(type.Id, "serial_number");
        var editorClient = _fixture.CreateClientWithRole("editor");

        var updated = await editorClient.PutAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields/{field.Id}",
            new UpdateResourceCustomFieldRequest { Label = "Nope" });
        Assert.Equal(HttpStatusCode.Forbidden, updated.StatusCode);

        var deleted = await editorClient.DeleteAsync(
            $"/api/resource-types/{type.Id}/custom-fields/{field.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
    }

    [Fact]
    public async Task GetOneCustomField_IsAllowedForEditors()
    {
        var type = await CreateTypeAsync();
        var field = await CreateFieldAsync(type.Id, "serial_number");
        var editorClient = _fixture.CreateClientWithRole("editor");

        var response = await editorClient.GetAsync(
            $"/api/resource-types/{type.Id}/custom-fields/{field.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomFields_IsAllowedForEditors()
    {
        // Editors fill the values in, so they must be able to see what the fields are.
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number");
        var editorClient = _fixture.CreateClientWithRole("editor");

        var response = await editorClient.GetAsync($"/api/resource-types/{type.Id}/custom-fields");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fields = (await response.Content.ReadFromJsonAsync<List<ResourceCustomFieldInfo>>())!;
        Assert.Single(fields);
    }
}
