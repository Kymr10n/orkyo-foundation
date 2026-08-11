using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// The values resources carry for their type's custom fields: typing, requiredness, and the
/// replace-vs-untouched semantics of an update.
/// </summary>
[Collection("Database collection")]
public class ResourceCustomFieldValueTests
{
    private readonly HttpClient _client;

    public ResourceCustomFieldValueTests(DatabaseFixture databaseFixture)
        => _client = databaseFixture.CreateAuthorizedClient();

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private async Task<ResourceTypeInfo> CreateTypeAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = $"machine_{Guid.NewGuid():N}",
            DisplayName = "Machine",
            DisplayNamePlural = "Machines",
        });
        return (await response.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;
    }

    private async Task CreateFieldAsync(
        Guid typeId, string key, string dataType, bool isRequired = false)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/resource-types/{typeId}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = key,
                Label = key,
                DataType = dataType,
                IsRequired = isRequired,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<HttpResponseMessage> CreateResourceAsync(
        string typeKey, Dictionary<string, JsonElement>? customFields, string name = "Lathe")
        => _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = typeKey,
            Name = name,
            AllocationMode = "Exclusive",
            CustomFields = customFields,
        });

    // ── create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateResource_PersistsAndReturnsValues()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text);
        await CreateFieldAsync(type.Id, "datasheet", CustomFieldDataTypes.Url);

        var response = await CreateResourceAsync(type.Key, new()
        {
            ["serial_number"] = Json("\"SN-42\""),
            ["datasheet"] = Json("\"https://example.com/sheet.pdf\""),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
        Assert.Equal("SN-42", created.CustomFields!["serial_number"].GetString());

        var fetched = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{created.Id}");
        Assert.Equal("https://example.com/sheet.pdf", fetched!.CustomFields!["datasheet"].GetString());
    }

    [Fact]
    public async Task CreateResource_AcceptsEveryDataType()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text);
        await CreateFieldAsync(type.Id, "capacity_kg", CustomFieldDataTypes.Number);
        await CreateFieldAsync(type.Id, "certified", CustomFieldDataTypes.Boolean);
        await CreateFieldAsync(type.Id, "purchased_on", CustomFieldDataTypes.Date);
        await CreateFieldAsync(type.Id, "datasheet", CustomFieldDataTypes.Url);

        var response = await CreateResourceAsync(type.Key, new()
        {
            ["serial_number"] = Json("\"SN-42\""),
            ["capacity_kg"] = Json("1200.5"),
            ["certified"] = Json("false"),
            ["purchased_on"] = Json("\"2026-08-11\""),
            ["datasheet"] = Json("\"https://example.com/sheet.pdf\""),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
        Assert.Equal(1200.5, created.CustomFields!["capacity_kg"].GetDouble());
        // False is an answer, not an absence — it round-trips rather than being dropped.
        Assert.False(created.CustomFields["certified"].GetBoolean());
        Assert.Equal("2026-08-11", created.CustomFields["purchased_on"].GetString());
    }

    [Fact]
    public async Task CreateResource_RejectsUnknownKey()
    {
        var type = await CreateTypeAsync();

        var response = await CreateResourceAsync(type.Key, new() { ["nope"] = Json("\"x\"") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(CustomFieldDataTypes.Number, "\"not a number\"")]
    [InlineData(CustomFieldDataTypes.Boolean, "\"yes\"")]
    [InlineData(CustomFieldDataTypes.Date, "\"31/12/2026\"")]
    [InlineData(CustomFieldDataTypes.Url, "\"javascript:alert(1)\"")]
    [InlineData(CustomFieldDataTypes.Url, "\"/relative/path\"")]
    [InlineData(CustomFieldDataTypes.Text, "42")]
    [InlineData(CustomFieldDataTypes.Text, "true")]
    [InlineData(CustomFieldDataTypes.Date, "20260811")]
    [InlineData(CustomFieldDataTypes.Url, "42")]
    // A document is not a value: nesting would let a "text" field carry an arbitrary payload.
    [InlineData(CustomFieldDataTypes.Text, "{\"nested\":\"object\"}")]
    [InlineData(CustomFieldDataTypes.Text, "[\"a\",\"b\"]")]
    // An exponent no double holds would be rejected by Postgres as an unmapped 500.
    [InlineData(CustomFieldDataTypes.Number, "1e1000000")]
    public async Task CreateResource_RejectsWronglyTypedValue(string dataType, string rawValue)
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "field", dataType);

        var response = await CreateResourceAsync(type.Key, new() { ["field"] = Json(rawValue) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_RejectsMissingRequiredFieldAndPersistsNothing()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text, isRequired: true);

        // Omitting the document entirely must not be a way around a required field.
        var response = await CreateResourceAsync(type.Key, customFields: null, name: "Unsaved lathe");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var listed = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/resources?resourceTypeKey={type.Key}");
        Assert.Equal(0, listed.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task CreateResource_TreatsBlankValueAsMissingForRequiredField()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text, isRequired: true);

        var response = await CreateResourceAsync(type.Key, new() { ["serial_number"] = Json("\"  \"") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(CustomFieldDataTypes.Text, 2001)]
    [InlineData(CustomFieldDataTypes.Url, 2049)]
    public async Task CreateResource_RejectsOverLongValue(string dataType, int length)
    {
        // Values ship back with every resource list read, so nothing may be unbounded.
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "field", dataType);
        var prefix = dataType == CustomFieldDataTypes.Url ? "https://example.com/" : "";
        var value = prefix + new string('x', length - prefix.Length);

        var response = await CreateResourceAsync(type.Key, new() { ["field"] = Json(JsonSerializer.Serialize(value)) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_TreatsJsonNullAsNoValue()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "notes", CustomFieldDataTypes.Text);
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text, isRequired: true);

        var response = await CreateResourceAsync(type.Key, new()
        {
            ["notes"] = Json("null"),
            ["serial_number"] = Json("null"),
        });

        // Null is empty for both purposes: allowed on the optional field, missing on the required one.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_AllowsBlankValueForOptionalField()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "notes", CustomFieldDataTypes.Text);

        var response = await CreateResourceAsync(type.Key, new() { ["notes"] = Json("\"\"") });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateResource_WithoutCustomFieldsLeavesValuesUntouched()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text, isRequired: true);
        var created = await CreateResourceAsync(type.Key, new() { ["serial_number"] = Json("\"SN-7\"") });
        var resource = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;

        // A rename must not have to restate every custom field, and must not trip the
        // required-field check by omitting them.
        var response = await _client.PutAsJsonAsync($"/api/resources/{resource.Id}",
            new UpdateResourceRequest { Name = "Renamed lathe" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
        Assert.Equal("Renamed lathe", updated.Name);
        Assert.Equal("SN-7", updated.CustomFields!["serial_number"].GetString());
    }

    [Fact]
    public async Task UpdateResource_ReplacesTheWholeValueDocument()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text);
        await CreateFieldAsync(type.Id, "notes", CustomFieldDataTypes.Text);
        var created = await CreateResourceAsync(type.Key, new()
        {
            ["serial_number"] = Json("\"SN-7\""),
            ["notes"] = Json("\"first\""),
        });
        var resource = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;

        var response = await _client.PutAsJsonAsync($"/api/resources/{resource.Id}",
            new UpdateResourceRequest
            {
                CustomFields = new() { ["serial_number"] = Json("\"SN-8\"") },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
        Assert.Equal("SN-8", updated.CustomFields!["serial_number"].GetString());
        Assert.False(updated.CustomFields.ContainsKey("notes"));
    }

    [Fact]
    public async Task UpdateResource_WithAnEmptyDocumentClearsEveryValue()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text);
        var created = await CreateResourceAsync(type.Key, new() { ["serial_number"] = Json("\"SN-7\"") });
        var resource = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;

        var response = await _client.PutAsJsonAsync($"/api/resources/{resource.Id}",
            new UpdateResourceRequest { CustomFields = new() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An empty document reads back as no values at all, the same shape a resource that
        // never had any reports.
        Assert.Null((await response.Content.ReadFromJsonAsync<ResourceInfo>())!.CustomFields);
    }

    [Fact]
    public async Task CreateResource_ReportsAnEmptyDocumentTheSameWayItReadsBack()
    {
        var type = await CreateTypeAsync();

        var response = await CreateResourceAsync(type.Key, new());
        var created = (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;

        var fetched = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{created.Id}");
        Assert.Null(created.CustomFields);
        Assert.Null(fetched!.CustomFields);
    }

    [Fact]
    public async Task DeletingTheTypeTakesItsFieldDefinitionsWithIt()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text);

        var deleted = await _client.DeleteAsync($"/api/resource-types/{type.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // The FK cascades, so the definitions cannot outlive the type they describe.
        var fields = await _client.GetAsync($"/api/resource-types/{type.Id}/custom-fields");
        Assert.Equal(HttpStatusCode.NotFound, fields.StatusCode);
    }

    [Fact]
    public async Task UpdateResource_RejectsDocumentMissingARequiredField()
    {
        var type = await CreateTypeAsync();
        await CreateFieldAsync(type.Id, "serial_number", CustomFieldDataTypes.Text, isRequired: true);
        await CreateFieldAsync(type.Id, "notes", CustomFieldDataTypes.Text);
        var created = await CreateResourceAsync(type.Key, new() { ["serial_number"] = Json("\"SN-7\"") });
        var resource = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;

        var response = await _client.PutAsJsonAsync($"/api/resources/{resource.Id}",
            new UpdateResourceRequest { CustomFields = new() { ["notes"] = Json("\"only notes\"") } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RetiringAField_LeavesResourcesEditable()
    {
        var type = await CreateTypeAsync();
        var fieldResponse = await _client.PostAsJsonAsync(
            $"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "serial_number",
                Label = "Serial number",
                DataType = CustomFieldDataTypes.Text,
                IsRequired = true,
            });
        var field = (await fieldResponse.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;
        var created = await CreateResourceAsync(type.Key, new() { ["serial_number"] = Json("\"SN-7\"") });
        var resource = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;

        await _client.PutAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields/{field.Id}",
            new UpdateResourceCustomFieldRequest { IsActive = false });

        // An inactive field is no longer required, and the value it left behind still validates.
        var response = await _client.PutAsJsonAsync($"/api/resources/{resource.Id}",
            new UpdateResourceRequest { CustomFields = new() { ["serial_number"] = Json("\"SN-7\"") } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
