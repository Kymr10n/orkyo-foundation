using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Constants;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// List definitions: the reusable shape a list takes. What is load-bearing here, and asserted,
/// is that reshaping a list is Admin-only while reading the shape is not, that a column's key
/// and data type never change once cells could exist behind them, and that deleting either a
/// definition in use or a column with data does the safe thing rather than the convenient one.
/// </summary>
[Collection("Database collection")]
public class ListDefinitionEndpointsTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;

    public ListDefinitionEndpointsTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";
    private static string UniqueKey(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private async Task<ListDefinitionInfo> CreateDefinitionAsync(string? name = null)
    {
        var response = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest { Name = name ?? UniqueName("Maintenance log") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ListDefinitionInfo>())!;
    }

    private async Task<ListColumnInfo> CreateColumnAsync(
        Guid definitionId, string dataType = ListColumnDataTypes.Text,
        IReadOnlyList<string>? options = null, bool isRequired = false, string? key = null)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/list-definitions/{definitionId}/columns",
            new CreateListColumnRequest
            {
                Key = key ?? UniqueKey("col"),
                Label = "Column",
                DataType = dataType,
                Options = options,
                IsRequired = isRequired,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ListColumnInfo>())!;
    }

    // ── definition lifecycle ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateDefinition_ReturnsItWithNoColumns()
    {
        var definition = await CreateDefinitionAsync();

        Assert.True(definition.IsActive);
        Assert.Empty(definition.Columns);
    }

    [Fact]
    public async Task GetDefinition_IncludesColumnsInFormOrder()
    {
        var definition = await CreateDefinitionAsync();
        await CreateColumnAsync(definition.Id, key: "second");
        await _client.PostAsJsonAsync($"/api/list-definitions/{definition.Id}/columns",
            new CreateListColumnRequest
            {
                Key = "first",
                Label = "First",
                DataType = ListColumnDataTypes.Text,
                SortOrder = -1,
            });

        var fetched = await _client.GetFromJsonAsync<ListDefinitionInfo>($"/api/list-definitions/{definition.Id}");

        Assert.Equal(2, fetched!.Columns.Count);
        Assert.Equal("first", fetched.Columns[0].Key);
    }

    [Fact]
    public async Task GetDefinitions_OmitsInactiveUnlessAsked()
    {
        var definition = await CreateDefinitionAsync();
        await _client.PutAsJsonAsync($"/api/list-definitions/{definition.Id}",
            new UpdateListDefinitionRequest { IsActive = false });

        var active = await _client.GetFromJsonAsync<List<ListDefinitionInfo>>("/api/list-definitions");
        var all = await _client.GetFromJsonAsync<List<ListDefinitionInfo>>("/api/list-definitions?includeInactive=true");

        Assert.DoesNotContain(active!, d => d.Id == definition.Id);
        Assert.Contains(all!, d => d.Id == definition.Id);
    }

    [Fact]
    public async Task CreateDefinition_RejectsADuplicateName()
    {
        var name = UniqueName("Components");
        await CreateDefinitionAsync(name);

        var response = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest { Name = name });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── columns ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateColumn_RejectsAnUnknownDataType()
    {
        var definition = await CreateDefinitionAsync();

        var response = await _client.PostAsJsonAsync($"/api/list-definitions/{definition.Id}/columns",
            new CreateListColumnRequest { Key = "k", Label = "K", DataType = "duration" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateColumn_RejectsASelectWithNoOptions()
    {
        var definition = await CreateDefinitionAsync();

        var response = await _client.PostAsJsonAsync($"/api/list-definitions/{definition.Id}/columns",
            new CreateListColumnRequest { Key = "status", Label = "Status", DataType = ListColumnDataTypes.Select });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateColumn_RejectsDuplicateOptions()
    {
        var definition = await CreateDefinitionAsync();

        var response = await _client.PostAsJsonAsync($"/api/list-definitions/{definition.Id}/columns",
            new CreateListColumnRequest
            {
                Key = "status",
                Label = "Status",
                DataType = ListColumnDataTypes.Select,
                Options = ["new", "new"],
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateColumn_RejectsOptionsOnANonSelectColumn()
    {
        var definition = await CreateDefinitionAsync();

        var response = await _client.PostAsJsonAsync($"/api/list-definitions/{definition.Id}/columns",
            new CreateListColumnRequest
            {
                Key = "mileage",
                Label = "Mileage",
                DataType = ListColumnDataTypes.Number,
                Options = ["a"],
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateColumn_RejectsOptionsOnANonSelectColumn()
    {
        var definition = await CreateDefinitionAsync();
        var column = await CreateColumnAsync(definition.Id, ListColumnDataTypes.Number);

        var response = await _client.PutAsJsonAsync(
            $"/api/list-definitions/{definition.Id}/columns/{column.Id}",
            new UpdateListColumnRequest { Options = ["a"] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateColumn_ChangesNeitherKeyNorDataType()
    {
        var definition = await CreateDefinitionAsync();
        var column = await CreateColumnAsync(definition.Id, ListColumnDataTypes.Text, key: "serial");

        // The update request has no key or dataType member at all — this pins that, so a later
        // "small addition" to the request type has to argue with a test.
        var properties = typeof(UpdateListColumnRequest).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Key", properties);
        Assert.DoesNotContain("DataType", properties);

        var updated = await _client.PutAsJsonAsync(
            $"/api/list-definitions/{definition.Id}/columns/{column.Id}",
            new UpdateListColumnRequest { Label = "Serial number" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var body = (await updated.Content.ReadFromJsonAsync<ListColumnInfo>())!;
        Assert.Equal("serial", body.Key);
        Assert.Equal(ListColumnDataTypes.Text, body.DataType);
    }

    [Fact]
    public async Task UpdateColumn_OnAnotherDefinition_IsNotFound()
    {
        var definition = await CreateDefinitionAsync();
        var other = await CreateDefinitionAsync();
        var column = await CreateColumnAsync(definition.Id);

        var response = await _client.PutAsJsonAsync(
            $"/api/list-definitions/{other.Id}/columns/{column.Id}",
            new UpdateListColumnRequest { Label = "Hijacked" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateColumn_ReplacesTheOptionsOfASelect()
    {
        var definition = await CreateDefinitionAsync();
        var column = await CreateColumnAsync(
            definition.Id, ListColumnDataTypes.Select, options: ["new", "used"]);

        var response = await _client.PutAsJsonAsync(
            $"/api/list-definitions/{definition.Id}/columns/{column.Id}",
            new UpdateListColumnRequest { Options = ["new", "used", "refurbished"] });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ListColumnInfo>())!;
        Assert.Equal(["new", "used", "refurbished"], updated.Options);
    }

    [Fact]
    public async Task UpdateColumn_RejectsEmptyOptionsOnASelect()
    {
        var definition = await CreateDefinitionAsync();
        var column = await CreateColumnAsync(
            definition.Id, ListColumnDataTypes.Select, options: ["new"]);

        // Emptying the options would leave a menu nothing can be chosen from — the same state
        // creation refuses, so editing refuses it too.
        var response = await _client.PutAsJsonAsync(
            $"/api/list-definitions/{definition.Id}/columns/{column.Id}",
            new UpdateListColumnRequest { Options = [] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemovingAnOption_LeavesRowsThatAlreadyUseItAlone()
    {
        // Matches criteria enum_values semantics: options are validated on write, never
        // retroactively — a row recorded under an option that is later withdrawn keeps its value
        // rather than being silently rewritten or made unreadable.
        var definition = await CreateDefinitionAsync();
        var column = await CreateColumnAsync(
            definition.Id, ListColumnDataTypes.Select, options: ["new", "used"], key: "condition");
        var instance = await CreateSharedInstanceAsync(definition.Id);

        var row = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest
            {
                Values = new Dictionary<string, JsonElement>
                {
                    ["condition"] = JsonDocument.Parse("\"used\"").RootElement,
                },
            });
        Assert.Equal(HttpStatusCode.Created, row.StatusCode);

        var narrowed = await _client.PutAsJsonAsync(
            $"/api/list-definitions/{definition.Id}/columns/{column.Id}",
            new UpdateListColumnRequest { Options = ["new"] });
        Assert.Equal(HttpStatusCode.OK, narrowed.StatusCode);

        var rows = await _client.GetFromJsonAsync<List<ListRowInfo>>($"/api/list-instances/{instance.Id}/rows");
        Assert.Equal("used", Assert.Single(rows!).Values["condition"].GetString());
    }

    // ── display column ────────────────────────────────────────────────────────

    [Fact]
    public async Task DisplayColumn_IsUnsetUntilDesignated_AndCanBeSetAndCleared()
    {
        var definition = await CreateDefinitionAsync();
        var column = await CreateColumnAsync(definition.Id, key: "part_name");

        Assert.Null(definition.DisplayColumnId);

        var set = await _client.PutAsJsonAsync($"/api/list-definitions/{definition.Id}",
            new UpdateListDefinitionRequest { DisplayColumnId = column.Id });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(column.Id, (await set.Content.ReadFromJsonAsync<ListDefinitionInfo>())!.DisplayColumnId);

        // Null on DisplayColumnId means "unchanged", so clearing needs its own flag.
        var cleared = await _client.PutAsJsonAsync($"/api/list-definitions/{definition.Id}",
            new UpdateListDefinitionRequest { ClearDisplayColumn = true });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<ListDefinitionInfo>())!.DisplayColumnId);
    }

    [Fact]
    public async Task DisplayColumn_RejectsAColumnOfAnotherDefinition()
    {
        var definition = await CreateDefinitionAsync();
        var other = await CreateDefinitionAsync();
        var strayColumn = await CreateColumnAsync(other.Id);

        // The FK cannot express "belongs to this definition" — it would name a cell these rows
        // do not have.
        var response = await _client.PutAsJsonAsync($"/api/list-definitions/{definition.Id}",
            new UpdateListDefinitionRequest { DisplayColumnId = strayColumn.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletingTheDisplayColumn_ClearsTheDesignation_AndKeepsTheDefinition()
    {
        var definition = await CreateDefinitionAsync();
        var column = await CreateColumnAsync(definition.Id, key: "part_name");
        await _client.PutAsJsonAsync($"/api/list-definitions/{definition.Id}",
            new UpdateListDefinitionRequest { DisplayColumnId = column.Id });

        var deleted = await _client.DeleteAsync($"/api/list-definitions/{definition.Id}/columns/{column.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // ON DELETE SET NULL, not CASCADE: losing the designated column must not take the
        // definition and every row built from it.
        var fetched = await _client.GetFromJsonAsync<ListDefinitionInfo>($"/api/list-definitions/{definition.Id}");
        Assert.NotNull(fetched);
        Assert.Null(fetched!.DisplayColumnId);
    }

    // ── delete semantics ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteDefinition_WhileAFieldBindsIt_IsAConflict()
    {
        var definition = await CreateDefinitionAsync();
        await CreateColumnAsync(definition.Id);

        var type = await CreateResourceTypeAsync();
        var field = await _client.PostAsJsonAsync($"/api/resource-types/{type.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "log",
                Label = "Log",
                DataType = CustomFieldDataTypes.List,
                ListDefinitionId = definition.Id,
            });
        Assert.Equal(HttpStatusCode.Created, field.StatusCode);

        var response = await _client.DeleteAsync($"/api/list-definitions/{definition.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteColumn_StripsItsCellsFromExistingRows()
    {
        var definition = await CreateDefinitionAsync();
        var keep = await CreateColumnAsync(definition.Id, key: "keep");
        var drop = await CreateColumnAsync(definition.Id, key: "drop");

        var instance = await CreateSharedInstanceAsync(definition.Id);
        var row = await _client.PostAsJsonAsync($"/api/list-instances/{instance.Id}/rows",
            new ListRowRequest
            {
                Values = new Dictionary<string, JsonElement>
                {
                    ["keep"] = JsonDocument.Parse("\"kept\"").RootElement,
                    ["drop"] = JsonDocument.Parse("\"dropped\"").RootElement,
                },
            });
        Assert.Equal(HttpStatusCode.Created, row.StatusCode);

        var deleted = await _client.DeleteAsync($"/api/list-definitions/{definition.Id}/columns/{drop.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var rows = await _client.GetFromJsonAsync<List<ListRowInfo>>($"/api/list-instances/{instance.Id}/rows");
        var stored = Assert.Single(rows!);
        Assert.True(stored.Values.ContainsKey("keep"));
        // The cell goes with the column: left behind, it would resurrect under a later column
        // that reused the key, with a different type and nothing to validate it.
        Assert.False(stored.Values.ContainsKey("drop"));
        Assert.NotEqual(Guid.Empty, keep.Id);
    }

    // ── authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Viewer_CanReadDefinitions_ButNotDefineThem()
    {
        var member = _fixture.CreateClientWithRole(RoleConstants.Viewer);

        var read = await member.GetAsync("/api/list-definitions");
        var write = await member.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest { Name = UniqueName("Nope") });

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Editor_CannotDefineAList()
    {
        var editor = _fixture.CreateClientWithRole(RoleConstants.Editor);

        // Reshaping a list is governance even for an editor: they fill lists in, they do not
        // decide what a list consists of.
        var response = await editor.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest { Name = UniqueName("Nope") });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    private async Task<ListInstanceInfo> CreateSharedInstanceAsync(Guid definitionId)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/list-definitions/{definitionId}/instances",
            new CreateListInstanceRequest { Name = UniqueName("Standard") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ListInstanceInfo>())!;
    }

    // ---- Scopes (migration 1810) --------------------------------------------------------

    [Fact]
    public async Task GetAll_ScopeFilter_ReturnsOnlyThatScope()
    {
        var common = await CreateDefinitionAsync();
        var orgResponse = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest
            {
                Name = UniqueName("Cost centres"),
                Scope = ListDefinitionScopes.Organization,
            });
        Assert.Equal(HttpStatusCode.Created, orgResponse.StatusCode);
        var org = (await orgResponse.Content.ReadFromJsonAsync<ListDefinitionInfo>())!;

        var response = await _client.GetAsync("/api/list-definitions?scope=organization");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var definitions = (await response.Content.ReadFromJsonAsync<List<ListDefinitionInfo>>())!;

        Assert.Contains(definitions, d => d.Id == org.Id);
        Assert.DoesNotContain(definitions, d => d.Id == common.Id);
        Assert.All(definitions, d => Assert.Equal(ListDefinitionScopes.Organization, d.Scope));
    }

    [Fact]
    public async Task GetAll_ScopeAndIncludeInactive_Combine()
    {
        var org = (await (await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest
            {
                Name = UniqueName("Retired org list"),
                Scope = ListDefinitionScopes.Organization,
            })).Content.ReadFromJsonAsync<ListDefinitionInfo>())!;
        var deactivate = await _client.PutAsJsonAsync($"/api/list-definitions/{org.Id}",
            new UpdateListDefinitionRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var activeOnly = (await (await _client.GetAsync("/api/list-definitions?scope=organization"))
            .Content.ReadFromJsonAsync<List<ListDefinitionInfo>>())!;
        var withInactive = (await (await _client.GetAsync(
                "/api/list-definitions?scope=organization&includeInactive=true"))
            .Content.ReadFromJsonAsync<List<ListDefinitionInfo>>())!;

        Assert.DoesNotContain(activeOnly, d => d.Id == org.Id);
        Assert.Contains(withInactive, d => d.Id == org.Id);
    }

    [Fact]
    public async Task GetAll_UnknownScope_Returns400()
    {
        var response = await _client.GetAsync("/api/list-definitions?scope=galactic");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDefinition_DefaultsToTheCommonScope()
    {
        // Every definition predating 1810 became 'common', so an unstated scope must agree.
        var definition = await CreateDefinitionAsync();

        Assert.Equal(ListDefinitionScopes.Common, definition.Scope);
        Assert.Null(definition.ResourceTypeId);
    }

    [Fact]
    public async Task CreateDefinition_AcceptsAResourceScopeWithItsType()
    {
        var type = await CreateResourceTypeAsync();

        var response = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest
            {
                Name = UniqueName("Tooling"),
                Scope = ListDefinitionScopes.Resource,
                ResourceTypeId = type.Id,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var definition = (await response.Content.ReadFromJsonAsync<ListDefinitionInfo>())!;
        Assert.Equal(ListDefinitionScopes.Resource, definition.Scope);
        Assert.Equal(type.Id, definition.ResourceTypeId);
    }

    [Fact]
    public async Task CreateDefinition_RejectsAResourceScopeWithNoType()
    {
        var response = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest
            {
                Name = UniqueName("Tooling"),
                Scope = ListDefinitionScopes.Resource,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(ListDefinitionScopes.Organization)]
    [InlineData(ListDefinitionScopes.Common)]
    public async Task CreateDefinition_RejectsATypeOnAScopeThatOwnsNone(string scope)
    {
        var type = await CreateResourceTypeAsync();

        var response = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest
            {
                Name = UniqueName("Departments"),
                Scope = scope,
                ResourceTypeId = type.Id,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDefinition_RejectsAnUnknownScope()
    {
        var response = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest { Name = UniqueName("Odd"), Scope = "tenant" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDefinition_RejectsAResourceScopeNamingAMissingType()
    {
        var response = await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest
            {
                Name = UniqueName("Tooling"),
                Scope = ListDefinitionScopes.Resource,
                ResourceTypeId = Guid.NewGuid(),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDefinition_AllowsTheSameNameUnderTwoResourceTypes()
    {
        // The point of scoping the namespace: "Certification" means different things on a mill
        // and on a person, and both tenants of the name must be able to exist.
        var first = await CreateResourceTypeAsync();
        var second = await CreateResourceTypeAsync();
        var name = UniqueName("Certification");

        foreach (var type in new[] { first, second })
        {
            var response = await _client.PostAsJsonAsync("/api/list-definitions",
                new CreateListDefinitionRequest
                {
                    Name = name,
                    Scope = ListDefinitionScopes.Resource,
                    ResourceTypeId = type.Id,
                });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    [Fact]
    public async Task CreateDefinition_StillRejectsADuplicateNameWithinOneType()
    {
        var type = await CreateResourceTypeAsync();
        var name = UniqueName("Certification");
        CreateListDefinitionRequest Request() => new()
        {
            Name = name,
            Scope = ListDefinitionScopes.Resource,
            ResourceTypeId = type.Id,
        };

        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/list-definitions", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsJsonAsync("/api/list-definitions", Request())).StatusCode);
    }

    [Fact]
    public async Task CreateDefinition_RejectsTwoCommonListsSharingAName()
    {
        // Postgres treats NULLs as distinct in a unique constraint, so the owner-less scopes need
        // a partial index rather than a plain UNIQUE. This is the test that would catch its loss.
        var name = UniqueName("Countries");

        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/list-definitions",
                new CreateListDefinitionRequest { Name = name })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsJsonAsync("/api/list-definitions",
                new CreateListDefinitionRequest { Name = name })).StatusCode);
    }

    [Fact]
    public async Task CreateDefinition_AllowsTheSameNameInCommonAndOrganization()
    {
        var name = UniqueName("Countries");

        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/list-definitions",
                new CreateListDefinitionRequest { Name = name })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/list-definitions",
                new CreateListDefinitionRequest
                {
                    Name = name,
                    Scope = ListDefinitionScopes.Organization,
                })).StatusCode);
    }

    [Fact]
    public async Task UpdateDefinition_RenamingIntoAnExistingNameInTheSameScope_Returns409()
    {
        var first = await CreateDefinitionAsync();
        var second = await CreateDefinitionAsync();

        var response = await _client.PutAsJsonAsync($"/api/list-definitions/{second.Id}",
            new UpdateListDefinitionRequest { Name = first.Name });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDefinition_RenamingIntoANameUsedByAnotherScope_Succeeds()
    {
        // The namespace is per scope since 1810, so a common list and an organization list are
        // free to share a name.
        var common = await CreateDefinitionAsync();
        var organization = (await (await _client.PostAsJsonAsync("/api/list-definitions",
            new CreateListDefinitionRequest
            {
                Name = UniqueName("Org"),
                Scope = ListDefinitionScopes.Organization,
            })).Content.ReadFromJsonAsync<ListDefinitionInfo>())!;

        var response = await _client.PutAsJsonAsync($"/api/list-definitions/{organization.Id}",
            new UpdateListDefinitionRequest { Name = common.Name });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
