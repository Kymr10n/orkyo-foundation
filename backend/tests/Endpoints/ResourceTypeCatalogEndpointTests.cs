using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// The type catalog: switches over pre-configured manufacturing types. What is load-bearing —
/// and asserted — is that activation adopts an existing row by key instead of colliding with
/// it, that re-activation never duplicates fields, that deactivation keeps data, and that the
/// purge takes the whole FK graph with it and nothing else.
/// </summary>
[Collection("Database collection")]
public class ResourceTypeCatalogEndpointTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;

    public ResourceTypeCatalogEndpointTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private sealed record CatalogEntryDto(
        string Key, string DisplayName, string DisplayNamePlural, string Description,
        string Icon, string Category, bool HasGeometry, bool HasDirectoryProfile,
        bool SingleGroupMembership, List<string> FieldLabels, string State,
        Guid? ResourceTypeId, string? TenantDisplayName, int ResourceCount, int RequestTargetCount);

    private async Task<List<CatalogEntryDto>> GetCatalogAsync()
    {
        var response = await _client.GetAsync("/api/resource-type-catalog");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<CatalogEntryDto>>())!;
    }

    private async Task<List<ResourceCustomFieldInfo>> GetFieldsAsync(Guid typeId) =>
        (await _client.GetFromJsonAsync<List<ResourceCustomFieldInfo>>(
            $"/api/resource-types/{typeId}/custom-fields"))!;

    [Fact]
    public async Task GetCatalog_ListsEveryEntryWithTenantState()
    {
        var catalog = await GetCatalogAsync();

        // All ten entries, both categories.
        Assert.Equal(10, catalog.Count);
        Assert.Contains(catalog, e => e.Key == "drill" && e.HasGeometry);
        Assert.Contains(catalog, e => e.Key == "forklift" && !e.HasGeometry);
        // The fixture's classic types map onto the catalog by key and read as activated.
        Assert.Equal("active", catalog.Single(e => e.Key == "person").State);
        Assert.Equal("active", catalog.Single(e => e.Key == "tool").State);
        Assert.All(catalog, e => Assert.NotEmpty(e.FieldLabels));
    }

    [Fact]
    public async Task Activate_AbsentEntry_CreatesTypeWithFlagsAndFields_Idempotently()
    {
        var first = await _client.PostAsync("/api/resource-type-catalog/lathe/activate", null);
        first.EnsureSuccessStatusCode();
        var type = (await first.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;

        Assert.Equal("lathe", type.Key);
        Assert.True(type.HasGeometry);
        Assert.True(type.SingleGroupMembership);
        Assert.False(type.IsSystem);
        var fields = await GetFieldsAsync(type.Id);
        Assert.Equal(5, fields.Count);
        Assert.Contains(fields, f => f.Key == "swing_over_bed" && f.DataType == "number");

        // Second flip of the same switch: same row, same field count.
        var second = await _client.PostAsync("/api/resource-type-catalog/lathe/activate", null);
        second.EnsureSuccessStatusCode();
        var again = (await second.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;
        Assert.Equal(type.Id, again.Id);
        Assert.Equal(5, (await GetFieldsAsync(type.Id)).Count);

        Assert.Equal("active", (await GetCatalogAsync()).Single(e => e.Key == "lathe").State);
    }

    [Fact]
    public async Task Activate_AdoptsAnExistingRow_KeepingRenameAndAddingOnlyMissingFields()
    {
        // The shared DB persists across runs; purge any leftover cnc so this is repeatable.
        await _client.DeleteAsync("/api/resource-type-catalog/cnc");

        // The tenant made this key first, under their own name and with one field of their own.
        var created = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = "cnc",
            DisplayName = "Bearbeitungszentrum",
            DisplayNamePlural = "Bearbeitungszentren",
            HasGeometry = true,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var existing = (await created.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;
        var own = await _client.PostAsJsonAsync($"/api/resource-types/{existing.Id}/custom-fields",
            new CreateResourceCustomFieldRequest
            {
                Key = "controller",
                Label = "Steuerung",
                DataType = "text",
            });
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);

        var activate = await _client.PostAsync("/api/resource-type-catalog/cnc/activate", null);
        activate.EnsureSuccessStatusCode();
        var adopted = (await activate.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;

        // Same row, the tenant's rename untouched; their `controller` field kept its label and
        // only the four missing catalog fields were added.
        Assert.Equal(existing.Id, adopted.Id);
        Assert.Equal("Bearbeitungszentrum", adopted.DisplayName);
        var fields = await GetFieldsAsync(existing.Id);
        Assert.Equal(5, fields.Count);
        Assert.Equal("Steuerung", fields.Single(f => f.Key == "controller").Label);

        var entry = (await GetCatalogAsync()).Single(e => e.Key == "cnc");
        Assert.Equal("Bearbeitungszentrum", entry.TenantDisplayName);
    }

    [Fact]
    public async Task Deactivate_KeepsData_AndReactivationRestoresIt()
    {
        var activate = await _client.PostAsync("/api/resource-type-catalog/workstation/activate", null);
        activate.EnsureSuccessStatusCode();
        var type = (await activate.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;
        var resource = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "workstation",
            Name = $"WS-{Guid.NewGuid():N}"[..16],
            Code = $"WS-{Guid.NewGuid():N}"[..12],
            AllocationMode = AllocationModes.Exclusive,
            CrossSiteAllowed = false,
            Geometry = new ResourceGeometry
            {
                Type = "rectangle",
                Coordinates = [new Coordinate { X = 0, Y = 0 }, new Coordinate { X = 2, Y = 2 }],
            },
        });
        Assert.Equal(HttpStatusCode.Created, resource.StatusCode);
        var resourceId = (await resource.Content.ReadFromJsonAsync<ResourceInfo>())!.Id;

        var off = await _client.PostAsync("/api/resource-type-catalog/workstation/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);

        // Hidden, not gone: new resources are refused, the old one still reads back.
        Assert.Equal("inactive", (await GetCatalogAsync()).Single(e => e.Key == "workstation").State);
        var refused = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "workstation",
            Name = "Refused",
            AllocationMode = AllocationModes.Exclusive,
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        (await _client.GetAsync($"/api/resources/{resourceId}")).EnsureSuccessStatusCode();

        var on = await _client.PostAsync("/api/resource-type-catalog/workstation/activate", null);
        on.EnsureSuccessStatusCode();
        Assert.Equal(type.Id, (await on.Content.ReadFromJsonAsync<ResourceTypeInfo>())!.Id);
        Assert.Equal("active", (await GetCatalogAsync()).Single(e => e.Key == "workstation").State);
    }

    [Fact]
    public async Task Purge_TakesTheWholeGraph_AndTheRequestSurvivesWithEmptyTargets()
    {
        // Build the FK-heavy graph: a type with a resource, an assignment on a request that
        // targets the type, a group with the resource as member, and availability-event
        // scopes of all three polymorphic kinds.
        var activate = await _client.PostAsync("/api/resource-type-catalog/drill/activate", null);
        activate.EnsureSuccessStatusCode();
        var type = (await activate.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;

        var resource = (await (await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "drill",
            Name = $"Drill-{Guid.NewGuid():N}"[..16],
            Code = $"DR-{Guid.NewGuid():N}"[..12],
            AllocationMode = AllocationModes.Exclusive,
            CrossSiteAllowed = false,
            Geometry = new ResourceGeometry
            {
                Type = "rectangle",
                Coordinates = [new Coordinate { X = 0, Y = 0 }, new Coordinate { X = 1, Y = 1 }],
            },
        })).Content.ReadFromJsonAsync<ResourceInfo>())!;

        var request = (await (await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"Drilling-{Guid.NewGuid():N}"[..20],
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
            TargetResourceTypeKeys = ["drill"],
            ResourceIds = [resource.Id],
            // A fixed far-past window: this request is site-neutral and outlives the purge,
            // and a site-neutral request counts under every site in the insights queries —
            // a now-spanning window would leak into their now-relative assertions.
            StartTs = new DateTime(2020, 1, 6, 9, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2020, 1, 6, 11, 0, 0, DateTimeKind.Utc),
        })).Content.ReadFromJsonAsync<RequestInfo>())!;

        var group = (await (await _client.PostAsJsonAsync("/api/resource-groups", new
        {
            resourceTypeKey = "drill",
            name = $"Drill line {Guid.NewGuid():N}"[..24],
        })).Content.ReadFromJsonAsync<ResourceGroupInfo>())!;
        (await _client.PutAsJsonAsync($"/api/resource-groups/{group.Id}/members", new
        {
            resourceIds = new[] { resource.Id },
        })).EnsureSuccessStatusCode();

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
            var orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
            await using var conn = connFactory.CreateOrgConnection(orgContext);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                WITH s AS (
                    INSERT INTO sites (name, code) VALUES ('Purge probe site', @siteCode) RETURNING id
                ), ev AS (
                    INSERT INTO availability_events (site_id, title, event_type, default_effect, start_ts, end_ts)
                    SELECT s.id, 'Purge probe', 'maintenance', 'closed', now(), now() + interval '1 day' FROM s
                    RETURNING id)
                INSERT INTO availability_event_scopes (availability_event_id, target_type, target_id, effect)
                SELECT ev.id, kind, target, 'closed' FROM ev,
                    (VALUES ('resource_type', @typeId), ('resource', @resourceId), ('resource_group', @groupId))
                    AS t(kind, target)", conn);
            cmd.Parameters.AddWithValue("siteCode", $"pp{Guid.NewGuid():N}"[..10]);
            cmd.Parameters.AddWithValue("typeId", type.Id);
            cmd.Parameters.AddWithValue("resourceId", resource.Id);
            cmd.Parameters.AddWithValue("groupId", group.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        var purge = await _client.DeleteAsync("/api/resource-type-catalog/drill");
        Assert.Equal(HttpStatusCode.OK, purge.StatusCode);
        var result = (await purge.Content.ReadFromJsonAsync<ResourceTypePurgeResult>())!;
        Assert.Equal(1, result.Resources);
        Assert.Equal(1, result.Assignments);
        Assert.Equal(1, result.Groups);
        Assert.True(result.RequestTargets >= 1);

        // Gone: the type, the resource; the request survives, targetless — a legal state.
        Assert.Equal("absent", (await GetCatalogAsync()).Single(e => e.Key == "drill").State);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/resources/{resource.Id}")).StatusCode);
        var survivingRequest = await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{request.Id}");
        Assert.NotNull(survivingRequest);

        // The polymorphic scopes went with the graph — no dangling rows.
        using var checkScope = _fixture.Factory.Services.CreateScope();
        var factory = checkScope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        var org = checkScope.ServiceProvider.GetRequiredService<OrgContext>();
        await using var check = factory.CreateOrgConnection(org);
        await check.OpenAsync();
        await using var scopeCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM availability_event_scopes WHERE target_id IN (@a, @b, @c)", check);
        scopeCmd.Parameters.AddWithValue("a", type.Id);
        scopeCmd.Parameters.AddWithValue("b", resource.Id);
        scopeCmd.Parameters.AddWithValue("c", group.Id);
        Assert.Equal(0L, await scopeCmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task UnknownKey_Returns400_ForEveryVerb()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsync("/api/resource-type-catalog/hovercraft/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsync("/api/resource-type-catalog/hovercraft/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.DeleteAsync("/api/resource-type-catalog/hovercraft")).StatusCode);
    }

    [Fact]
    public async Task KnownKeyWithNoRow_Returns404_ForDeactivateAndPurge()
    {
        // Other suites may have created mill (the machine seed does) — purge first, so the
        // absence this test asserts is its own doing rather than an ordering accident.
        await _client.DeleteAsync("/api/resource-type-catalog/mill");

        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsync("/api/resource-type-catalog/mill/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.DeleteAsync("/api/resource-type-catalog/mill")).StatusCode);
    }

    [Fact]
    public async Task Writes_RequireAdmin_ReadsStayMemberOpen()
    {
        var viewer = _fixture.CreateClientWithRole(RoleConstants.Viewer);
        var editor = _fixture.CreateClientWithRole(RoleConstants.Editor);

        (await viewer.GetAsync("/api/resource-type-catalog")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsync("/api/resource-type-catalog/forklift/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await editor.PostAsync("/api/resource-type-catalog/forklift/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await editor.DeleteAsync("/api/resource-type-catalog/forklift")).StatusCode);
    }
}
