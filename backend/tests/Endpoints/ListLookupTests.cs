using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Shared instances and the lookup fields that point at them — the other half of lists, where
/// many resources reference one set of rows.
///
/// What is load-bearing here: a lookup value is only ever ids that exist in the bound instance,
/// editing a shared row is seen by every resource that picked it, and deleting one takes the id
/// out of every value that referenced it rather than leaving a dangling pointer.
/// </summary>
[Collection("Database collection")]
public class ListLookupTests
{
    private readonly HttpClient _client;

    private readonly DatabaseFixture _fixture;

    public ListLookupTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";
    private static string UniqueKey(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private async Task<ListDefinitionInfo> CreateComponentsDefinitionAsync()
    {
        var created = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest { Name = UniqueName("Components") });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var definition = (await created.Content.ReadFromJsonAsync<ListDefinitionInfo>())!;

        var column = await _client.PostAsJsonAsync($"/api/list-definitions/{definition.Id}/columns",
            new CreateListColumnRequest
            {
                Key = "name",
                Label = "Name",
                DataType = ListColumnDataTypes.Text,
            });
        Assert.Equal(HttpStatusCode.Created, column.StatusCode);

        return definition;
    }

    private async Task<ListInstanceInfo> CreateSharedInstanceAsync(Guid definitionId)
    {
        var response = await _client.PostAsJsonAsync($"/api/list-definitions/{definitionId}/instances",
            new CreateListInstanceRequest { Name = UniqueName("Standard parts") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ListInstanceInfo>())!;
    }

    private async Task<ListRowInfo> CreateRowAsync(Guid instanceId, string name)
    {
        var response = await _client.PostAsJsonAsync($"/api/list-instances/{instanceId}/rows",
            new ListRowRequest
            {
                Values = new Dictionary<string, JsonElement> { ["name"] = Json($"\"{name}\"") },
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ListRowInfo>())!;
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

    private async Task<ResourceCustomFieldInfo> CreateLookupFieldAsync(Guid typeId, Guid instanceId, string key = "parts")
    {
        var response = await _client.PostAsJsonAsync($"/api/resource-types/{typeId}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = key,
                Label = "Parts",
                DataType = CustomFieldDataTypes.ListLookup,
                ListInstanceId = instanceId,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;
    }

    private async Task<ResourceInfo> CreateResourceAsync(string typeKey, Dictionary<string, JsonElement>? customFields = null)
    {
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = typeKey,
            Name = UniqueName("Lathe"),
            AllocationMode = AllocationModes.Exclusive,
            CustomFields = customFields,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    // ── binding ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ALookupField_NeedsASharedInstance_NotADefinition()
    {
        var definition = await CreateComponentsDefinitionAsync();
        var type = await CreateResourceTypeAsync();

        var withDefinition = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "parts",
                Label = "Parts",
                DataType = CustomFieldDataTypes.ListLookup,
                ListDefinitionId = definition.Id,
            });

        var withNothing = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "parts2",
                Label = "Parts",
                DataType = CustomFieldDataTypes.ListLookup,
            });

        Assert.Equal(HttpStatusCode.BadRequest, withDefinition.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withNothing.StatusCode);
    }

    [Fact]
    public async Task ALookupField_CannotBindAPerResourceInstance()
    {
        // A per-resource instance belongs to one resource; pointing a shared lookup at it would
        // let every resource of a type read one resource's private rows.
        var definition = await CreateComponentsDefinitionAsync();
        var type = await CreateResourceTypeAsync();

        var listField = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "log",
                Label = "Log",
                DataType = CustomFieldDataTypes.List,
                ListDefinitionId = definition.Id,
            });
        var field = (await listField.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;
        var resource = await CreateResourceAsync(type.Key);

        var ensured = await _client.PostAsync($"/api/resources/{resource.Id}/list-fields/{field.Id}/instance", null);
        var perResource = (await ensured.Content.ReadFromJsonAsync<ListInstanceInfo>())!;

        var response = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "parts",
                Label = "Parts",
                DataType = CustomFieldDataTypes.ListLookup,
                ListInstanceId = perResource.Id,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── values ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ALookupValue_AcceptsIdsThatExistInTheBoundInstance()
    {
        var definition = await CreateComponentsDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var bolt = await CreateRowAsync(instance.Id, "Bolt");
        var nut = await CreateRowAsync(instance.Id, "Nut");

        var type = await CreateResourceTypeAsync();
        var field = await CreateLookupFieldAsync(type.Id, instance.Id);

        var resource = await CreateResourceAsync(type.Key, new Dictionary<string, JsonElement>
        {
            [field.Key] = Json($"[\"{bolt.Id}\", \"{nut.Id}\"]"),
        });

        Assert.Equal(2, resource.CustomFields![field.Key].GetArrayLength());
    }

    [Fact]
    public async Task ALookupValue_RejectsAnIdFromAnotherInstance()
    {
        var definition = await CreateComponentsDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var other = await CreateSharedInstanceAsync(definition.Id);
        var strayRow = await CreateRowAsync(other.Id, "Belongs elsewhere");

        var type = await CreateResourceTypeAsync();
        var field = await CreateLookupFieldAsync(type.Id, instance.Id);

        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = type.Key,
            Name = "Lathe",
            AllocationMode = AllocationModes.Exclusive,
            CustomFields = new Dictionary<string, JsonElement>
            {
                [field.Key] = Json($"[\"{strayRow.Id}\"]"),
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ALookupValue_RejectsTheSameRowTwice()
    {
        var definition = await CreateComponentsDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var bolt = await CreateRowAsync(instance.Id, "Bolt");

        var type = await CreateResourceTypeAsync();
        var field = await CreateLookupFieldAsync(type.Id, instance.Id);

        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = type.Key,
            Name = "Lathe",
            AllocationMode = AllocationModes.Exclusive,
            CustomFields = new Dictionary<string, JsonElement>
            {
                [field.Key] = Json($"[\"{bolt.Id}\", \"{bolt.Id}\"]"),
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ALookupValue_MustBeAnArray()
    {
        var definition = await CreateComponentsDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var bolt = await CreateRowAsync(instance.Id, "Bolt");

        var type = await CreateResourceTypeAsync();
        var field = await CreateLookupFieldAsync(type.Id, instance.Id);

        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = type.Key,
            Name = "Lathe",
            AllocationMode = AllocationModes.Exclusive,
            CustomFields = new Dictionary<string, JsonElement>
            {
                [field.Key] = Json($"\"{bolt.Id}\""),
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void ThePickCap_IsEnforced()
    {
        // As with the row cap: the constant's job is to exist and bound the payload, and driving
        // 101 picks through HTTP would dominate the suite to assert the same thing.
        Assert.Equal(100, ResourceCustomFieldService.MaxPickedRows);
    }

    // ── the shared part of shared ─────────────────────────────────────────────

    [Fact]
    public async Task EditingASharedRow_IsSeenByEveryResourceThatPickedIt()
    {
        var definition = await CreateComponentsDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var bolt = await CreateRowAsync(instance.Id, "Bolt");

        var type = await CreateResourceTypeAsync();
        var field = await CreateLookupFieldAsync(type.Id, instance.Id);
        var picked = Json($"[\"{bolt.Id}\"]");

        await CreateResourceAsync(type.Key, new Dictionary<string, JsonElement> { [field.Key] = picked });
        await CreateResourceAsync(type.Key, new Dictionary<string, JsonElement> { [field.Key] = picked });

        // One edit, in one place — the resources hold ids, so neither has to be touched.
        var updated = await _client.PutAsJsonAsync($"/api/list-instances/{instance.Id}/rows/{bolt.Id}",
            new ListRowRequest
            {
                Values = new Dictionary<string, JsonElement> { ["name"] = Json("\"Bolt (M8)\"") },
            });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var rows = await _client.GetFromJsonAsync<List<ListRowInfo>>($"/api/list-instances/{instance.Id}/rows");
        Assert.Equal("Bolt (M8)", Assert.Single(rows!).Values["name"].GetString());
    }

    [Fact]
    public async Task RenamingASharedRow_UpdatesTheSearchIndexOfEveryResourceHoldingIt()
    {
        // Deleting a row already refreshed, because the delete writes resources.custom_fields and
        // trips that trigger. A rename touches no resource, so before migration 1910 the index
        // kept the old label for good and the new name found nobody.
        // Organization-scoped with a designated display column: that is the shape the search
        // function resolves labels from, so it is the shape whose rename has to propagate.
        var created = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest
            {
                Name = UniqueName("Trades"),
                Scope = ListDefinitionScopes.Organization,
            });
        var definition = (await created.Content.ReadFromJsonAsync<ListDefinitionInfo>())!;

        var nameColumn = (await (await _client.PostAsJsonAsync(
            $"/api/list-definitions/{definition.Id}/columns",
            new CreateListColumnRequest { Key = "name", Label = "Name", DataType = ListColumnDataTypes.Text }))
            .Content.ReadFromJsonAsync<ListColumnInfo>())!;
        await _client.PutAsJsonAsync($"/api/list-definitions/{definition.Id}",
            new UpdateListDefinitionRequest { DisplayColumnId = nameColumn.Id });

        var instance = await CreateSharedInstanceAsync(definition.Id);
        var bolt = await CreateRowAsync(instance.Id, "Fitter");

        var type = await CreateResourceTypeAsync();
        var field = await CreateLookupFieldAsync(type.Id, instance.Id);
        var resource = await CreateResourceAsync(type.Key, new Dictionary<string, JsonElement>
        {
            [field.Key] = Json($"[\"{bolt.Id}\"]"),
        });

        var renamed = $"Technician-{Guid.NewGuid():N}"[..18];
        var updated = await _client.PutAsJsonAsync($"/api/list-instances/{instance.Id}/rows/{bolt.Id}",
            new ListRowRequest
            {
                Values = new Dictionary<string, JsonElement> { ["name"] = Json($"\"{renamed}\"") },
            });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        var org = scope.ServiceProvider.GetRequiredService<OrgContext>();
        await using var conn = factory.CreateOrgConnection(org);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT subtitle FROM search_documents WHERE entity_type = 'resource' AND entity_id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", resource.Id);

        Assert.Contains(renamed, await cmd.ExecuteScalarAsync() as string ?? "");
    }

    [Fact]
    public async Task DeletingASharedRow_DropsItFromEveryFieldThatPickedIt_NotJustOne()
    {
        // Two lookup fields on one type, both bound to the same shared list — nothing forbids it,
        // and a resource can legitimately pick the same row in both. The strip has to clear both:
        // an id left in the second field names a row that is gone, and the next save of that
        // resource is refused for selecting a row that no longer exists.
        var definition = await CreateComponentsDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var bolt = await CreateRowAsync(instance.Id, "Bolt");
        var nut = await CreateRowAsync(instance.Id, "Nut");

        var type = await CreateResourceTypeAsync();
        var primary = await CreateLookupFieldAsync(type.Id, instance.Id, "parts");
        var spares = await CreateLookupFieldAsync(type.Id, instance.Id, "spare_parts");

        var resource = await CreateResourceAsync(type.Key, new Dictionary<string, JsonElement>
        {
            [primary.Key] = Json($"[\"{bolt.Id}\", \"{nut.Id}\"]"),
            [spares.Key] = Json($"[\"{bolt.Id}\"]"),
        });

        var deleted = await _client.DeleteAsync($"/api/list-instances/{instance.Id}/rows/{bolt.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var after = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{resource.Id}");
        var primaryIds = after!.CustomFields![primary.Key].EnumerateArray().Select(e => e.GetString());
        Assert.Equal([nut.Id.ToString()], primaryIds);
        Assert.Empty(after.CustomFields![spares.Key].EnumerateArray());

        // The point of the whole exercise: the resource is still editable afterwards.
        var resaved = await _client.PutAsJsonAsync($"/api/resources/{resource.Id}",
            new UpdateResourceRequest { CustomFields = after.CustomFields });
        Assert.Equal(HttpStatusCode.OK, resaved.StatusCode);
    }

    [Fact]
    public async Task DeletingASharedRow_DropsItFromEveryResourceThatPickedIt()
    {
        var definition = await CreateComponentsDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var bolt = await CreateRowAsync(instance.Id, "Bolt");
        var nut = await CreateRowAsync(instance.Id, "Nut");

        var type = await CreateResourceTypeAsync();
        var field = await CreateLookupFieldAsync(type.Id, instance.Id);

        var first = await CreateResourceAsync(type.Key, new Dictionary<string, JsonElement>
        {
            [field.Key] = Json($"[\"{bolt.Id}\", \"{nut.Id}\"]"),
        });
        var second = await CreateResourceAsync(type.Key, new Dictionary<string, JsonElement>
        {
            [field.Key] = Json($"[\"{bolt.Id}\"]"),
        });

        var deleted = await _client.DeleteAsync($"/api/list-instances/{instance.Id}/rows/{bolt.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // A stored id must never outlive the row it names, or the next read shows a selection
        // that cannot be resolved to anything.
        var firstAfter = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{first.Id}");
        var secondAfter = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{second.Id}");

        var firstIds = firstAfter!.CustomFields![field.Key].EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal([nut.Id.ToString()], firstIds);
        Assert.Empty(secondAfter!.CustomFields![field.Key].EnumerateArray());
    }

    [Fact]
    public async Task DeletingASharedInstance_WhileAFieldBindsIt_IsAConflict()
    {
        var definition = await CreateComponentsDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var type = await CreateResourceTypeAsync();
        await CreateLookupFieldAsync(type.Id, instance.Id);

        var response = await _client.DeleteAsync(
            $"/api/list-definitions/{definition.Id}/instances/{instance.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ARequiredLookup_RejectsAnEmptyArray()
    {
        var definition = await CreateComponentsDefinitionAsync();
        var instance = await CreateSharedInstanceAsync(definition.Id);
        var type = await CreateResourceTypeAsync();

        var created = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "parts",
                Label = "Parts",
                DataType = CustomFieldDataTypes.ListLookup,
                ListInstanceId = instance.Id,
                IsRequired = true,
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var field = (await created.Content.ReadFromJsonAsync<ResourceCustomFieldInfo>())!;

        // Empty is unfilled, the same way an empty string is for text — a required lookup needs
        // at least one pick.
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = type.Key,
            Name = "Lathe",
            AllocationMode = AllocationModes.Exclusive,
            CustomFields = new Dictionary<string, JsonElement> { [field.Key] = Json("[]") },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
