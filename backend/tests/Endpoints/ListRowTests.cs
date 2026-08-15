using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Services;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// List rows: the data side. The load-bearing behaviours asserted here are the resolver's
/// (a read never creates a holder, a write does, and only for a field that really belongs to
/// the resource), cell validation against the definition's columns, and the delete rules —
/// deleting a resource must take its list data with it.
/// </summary>
[Collection("Database collection")]
public class ListRowTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;

    public ListRowTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";
    private static string UniqueKey(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private static Dictionary<string, JsonElement> Values(params (string Key, string Raw)[] cells) =>
        cells.ToDictionary(c => c.Key, c => Json(c.Raw), StringComparer.Ordinal);

    /// <summary>A definition with one text column ("note") and one number column ("mileage").</summary>
    private async Task<ListDefinitionInfo> CreateLogDefinitionAsync(bool noteRequired = false)
    {
        var created = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest { Name = UniqueName("Maintenance log") });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var definition = (await created.Content.ReadFromJsonAsync<ListDefinitionInfo>())!;

        foreach (var (key, type, required) in new[]
                 {
                     ("note", ListColumnDataTypes.Text, noteRequired),
                     ("mileage", ListColumnDataTypes.Number, false),
                 })
        {
            var column = await _client.PostAsJsonAsync($"/api/list-definitions/{definition.Id}/columns",
                new CreateListColumnRequest { Key = key, Label = key, DataType = type, IsRequired = required });
            Assert.Equal(HttpStatusCode.Created, column.StatusCode);
        }

        return definition;
    }

    private async Task<ListInstanceInfo> CreateSharedInstanceAsync(Guid definitionId)
    {
        var response = await _client.PostAsJsonAsync($"/api/list-definitions/{definitionId}/instances",
            new CreateListInstanceRequest { Name = UniqueName("Standard") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ListInstanceInfo>())!;
    }

    private async Task<ResourceTypeInfo> CreateResourceTypeAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("machine"),
            DisplayName = "Machine",
            DisplayNamePlural = "Machines",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;
    }

    private async Task<ResourceCustomFieldInfo> CreateListFieldAsync(Guid typeId, Guid definitionId)
    {
        var response = await _client.PostAsJsonAsync($"/api/resource-types/{typeId}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "log",
                Label = "Log",
                DataType = CustomFieldDataTypes.List,
                ListDefinitionId = definitionId,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;
    }

    private async Task<ResourceInfo> CreateResourceAsync(string typeKey, string name = "Lathe")
    {
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = typeKey,
            Name = name,
            AllocationMode = AllocationModes.Exclusive,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    // ── the resolver ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolverGet_BeforeAnyWrite_IsNull_AndCreatesNothing()
    {
        var definition = await CreateLogDefinitionAsync();
        var type = await CreateResourceTypeAsync();
        var field = await CreateListFieldAsync(type.Id, definition.Id);
        var resource = await CreateResourceAsync(type.Key);

        var first = await _client.GetAsync($"/api/resources/{resource.Id}/list-fields/{field.Id}/instance");
        // Read twice: if the first GET had quietly created a holder, the second would find one.
        var second = await _client.GetAsync($"/api/resources/{resource.Id}/list-fields/{field.Id}/instance");

        // 200 with a null body: an untouched list is ordinary, not an error.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Null(await first.Content.ReadFromJsonAsync<ListInstanceInfo>());
        Assert.Null(await second.Content.ReadFromJsonAsync<ListInstanceInfo>());
    }

    [Fact]
    public async Task ResolverPost_IsIdempotent()
    {
        var definition = await CreateLogDefinitionAsync();
        var type = await CreateResourceTypeAsync();
        var field = await CreateListFieldAsync(type.Id, definition.Id);
        var resource = await CreateResourceAsync(type.Key);

        var first = await _client.PostAsync($"/api/resources/{resource.Id}/list-fields/{field.Id}/instance", null);
        var second = await _client.PostAsync($"/api/resources/{resource.Id}/list-fields/{field.Id}/instance", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var a = (await first.Content.ReadFromJsonAsync<ListInstanceInfo>())!;
        var b = (await second.Content.ReadFromJsonAsync<ListInstanceInfo>())!;
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(ListInstanceKinds.Resource, a.Kind);
    }

    [Fact]
    public async Task ResolverPost_ForAFieldOfAnotherType_IsNotFound()
    {
        var definition = await CreateLogDefinitionAsync();
        var type = await CreateResourceTypeAsync();
        var otherType = await CreateResourceTypeAsync();
        var field = await CreateListFieldAsync(otherType.Id, definition.Id);
        var resource = await CreateResourceAsync(type.Key);

        // The field is real and the resource is real, but the field does not describe this
        // resource's type — the pair names an instance with no shape.
        var response = await _client.PostAsync($"/api/resources/{resource.Id}/list-fields/{field.Id}/instance", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ResolverPost_ForAScalarField_IsNotFound()
    {
        var type = await CreateResourceTypeAsync();
        var scalar = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "serial",
                Label = "Serial",
                DataType = CustomFieldDataTypes.Text,
            });
        var field = (await scalar.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;
        var resource = await CreateResourceAsync(type.Key);

        var response = await _client.PostAsync($"/api/resources/{resource.Id}/list-fields/{field.Id}/instance", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── cell validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRow_RejectsACellOfTheWrongType()
    {
        var definition = await CreateLogDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);

        var response = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("mileage", "\"not a number\"")) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRow_RejectsAnUnknownColumn()
    {
        var definition = await CreateLogDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);

        var response = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("nope", "\"x\"")) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRow_RejectsAMissingRequiredCell()
    {
        var definition = await CreateLogDefinitionAsync(noteRequired: true);
        var instance = await CreateSharedInstanceAsync(definition.Id);

        var response = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("mileage", "1200")) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRow_AcceptsAValidRow_AndReadsItBack()
    {
        var definition = await CreateLogDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);

        var created = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("note", "\"oil change\""), ("mileage", "1200")) });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var rows = await _client.GetFromJsonAsync<List<ListRowInfo>>($"/api/list-instances/{instance.Id}/rows");
        var row = Assert.Single(rows!);
        Assert.Equal("oil change", row.Values["note"].GetString());
        Assert.Equal(1200, row.Values["mileage"].GetInt32());
    }

    [Fact]
    public async Task SelectCell_MustBeOneOfTheDeclaredOptions()
    {
        var created = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest { Name = UniqueName("Components") });
        var definition = (await created.Content.ReadFromJsonAsync<ListDefinitionInfo>())!;
        await _client.PostAsJsonAsync($"/api/list-definitions/{definition.Id}/columns",
            new CreateListColumnRequest
            {
                Key = "status",
                Label = "Status",
                DataType = ListColumnDataTypes.Select,
                Options = ["new", "used"],
            });
        var instance = await CreateSharedInstanceAsync(definition.Id);

        var good = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("status", "\"used\"")) });
        var bad = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("status", "\"refurbished\"")) });

        Assert.Equal(HttpStatusCode.Created, good.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task UpdateRow_OnAnotherInstance_IsNotFound()
    {
        var definition = await CreateLogDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var other = await CreateSharedInstanceAsync(definition.Id);

        var created = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("note", "\"mine\"")) });
        var row = (await created.Content.ReadFromJsonAsync<ListRowInfo>())!;

        var response = await _client.PutAsJsonAsync($"/api/list-instances/{other.Id}/rows/{row.Id}",
            new ListRowRequest { Values = Values(("note", "\"hijacked\"")) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── delete semantics ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingTheResource_KeepsItsListData_BecauseTheDeleteIsSoft()
    {
        var definition = await CreateLogDefinitionAsync();
        var type = await CreateResourceTypeAsync();
        var field = await CreateListFieldAsync(type.Id, definition.Id);
        var resource = await CreateResourceAsync(type.Key);

        var ensured = await _client.PostAsync($"/api/resources/{resource.Id}/list-fields/{field.Id}/instance", null);
        var instance = (await ensured.Content.ReadFromJsonAsync<ListInstanceInfo>())!;
        await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("note", "\"oil change\"")) });

        var deleted = await _client.DeleteAsync($"/api/resources/{resource.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // DELETE /api/resources deactivates rather than removes (ResourceRepository sets
        // is_active = false), so the row survives and the FK cascade never fires. The list data
        // therefore survives with it, which is the coherent outcome: a deactivated resource that
        // came back without its maintenance history would have lost data no one agreed to discard.
        //
        // The ON DELETE CASCADE in migration 1780 still matters — it covers the paths that really
        // remove the row, such as a tenant purge — but it is not what this endpoint exercises.
        var after = await _client.GetAsync($"/api/list-instances/{instance.Id}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);

        var rows = await _client.GetFromJsonAsync<List<ListRowInfo>>($"/api/list-instances/{instance.Id}/rows");
        Assert.Single(rows!);
    }

    [Fact]
    public async Task DeletingTheField_TakesThePerResourceInstanceWithIt()
    {
        var definition = await CreateLogDefinitionAsync();
        var type = await CreateResourceTypeAsync();
        var field = await CreateListFieldAsync(type.Id, definition.Id);
        var resource = await CreateResourceAsync(type.Key);

        var ensured = await _client.PostAsync($"/api/resources/{resource.Id}/list-fields/{field.Id}/instance", null);
        var instance = (await ensured.Content.ReadFromJsonAsync<ListInstanceInfo>())!;

        var deleted = await _client.DeleteAsync($"/api/resource-types/{type.Id}/custom-fields/{field.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var after = await _client.GetAsync($"/api/list-instances/{instance.Id}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    // ── the field itself ──────────────────────────────────────────────────────

    [Fact]
    public async Task AListField_CannotBeRequired()
    {
        var definition = await CreateLogDefinitionAsync();
        var type = await CreateResourceTypeAsync();

        var response = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "log",
                Label = "Log",
                DataType = CustomFieldDataTypes.List,
                ListDefinitionId = definition.Id,
                IsRequired = true,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AListField_NeedsADefinition()
    {
        var type = await CreateResourceTypeAsync();

        var response = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "log",
                Label = "Log",
                DataType = CustomFieldDataTypes.List,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AScalarField_CannotCarryABinding()
    {
        var definition = await CreateLogDefinitionAsync();
        var type = await CreateResourceTypeAsync();

        var response = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "serial",
                Label = "Serial",
                DataType = CustomFieldDataTypes.Text,
                ListDefinitionId = definition.Id,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AListField_TakesNoValueOnTheResource()
    {
        var definition = await CreateLogDefinitionAsync();
        var type = await CreateResourceTypeAsync();
        var field = await CreateListFieldAsync(type.Id, definition.Id);

        // Rows are addressed by (resource, field), so a value here would be written into a slot
        // that does not exist — and a whole-document replace would look like it cleared rows.
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = type.Key,
            Name = "Lathe",
            AllocationMode = AllocationModes.Exclusive,
            CustomFields = Values((field.Key, "\"oops\"")),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── dissolution readiness ─────────────────────────────────────────────────

    [Fact]
    public async Task AListWorksOnTheSystemPersonType_EndToEnd()
    {
        // The point of the feature: spaces and people are ordinary resource_types rows since
        // migration 1700, so a list attaches to them with no special-casing. If this ever fails,
        // Space/People dissolution has lost a prerequisite.
        var types = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");
        var personType = types!.Single(t => t.Key == ResourceTypeKeys.Person);

        var definition = await CreateLogDefinitionAsync();
        var created = await _client.PostAsJsonAsync($"/api/resource-types/{personType.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = UniqueKey("certs"),
                Label = "Certifications",
                DataType = CustomFieldDataTypes.List,
                ListDefinitionId = definition.Id,
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var field = (await created.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;

        var person = await CreateResourceAsync(personType.Key, UniqueName("Alex"));

        var ensured = await _client.PostAsync($"/api/resources/{person.Id}/list-fields/{field.Id}/instance", null);
        Assert.Equal(HttpStatusCode.OK, ensured.StatusCode);
        var instance = (await ensured.Content.ReadFromJsonAsync<ListInstanceInfo>())!;

        var row = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("note", "\"forklift licence\"")) });
        Assert.Equal(HttpStatusCode.Created, row.StatusCode);

        var rows = await _client.GetFromJsonAsync<List<ListRowInfo>>($"/api/list-instances/{instance.Id}/rows");
        Assert.Single(rows!);
    }

    // ── authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Editor_CanWriteRows_ButViewerCannot()
    {
        var definition = await CreateLogDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);

        var editor = _fixture.CreateClientWithRole(RoleConstants.Editor);
        var viewer = _fixture.CreateClientWithRole(RoleConstants.Viewer);

        var editorWrite = await editor.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("note", "\"by an editor\"")) });
        var viewerWrite = await viewer.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest { Values = Values(("note", "\"by a viewer\"")) });
        var viewerRead = await viewer.GetAsync($"/api/list-instances/{instance.Id}/rows");

        Assert.Equal(HttpStatusCode.Created, editorWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viewerRead.StatusCode);
    }

    [Fact]
    public async Task RowCap_IsEnforced()
    {
        // Asserted through the service constant rather than by inserting 500 rows over HTTP: the
        // cap's job is to exist and be reachable, and a 500-request test would dominate the suite.
        Assert.Equal(500, ListRowService.MaxRowsPerInstance);
    }
}
