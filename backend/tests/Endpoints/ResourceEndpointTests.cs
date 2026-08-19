using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Endpoints;
using Api.Models;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ResourceEndpointTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;

    public ResourceEndpointTests(DatabaseFixture databaseFixture)
    {
        _fixture = databaseFixture;
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private async Task<ResourceInfo> CreatePersonAsync(string name = "Test Person")
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = name,
            AllocationMode = "Fractional",
            BaseAvailabilityPercent = 100,
        };
        var response = await _client.PostAsJsonAsync("/api/resources", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    [Fact]
    public async Task CreateResource_Person_Returns201()
    {
        var r = await CreatePersonAsync($"Person-{Guid.NewGuid():N}"[..20]);
        Assert.Equal("person", r.ResourceTypeKey);
        Assert.Equal("Fractional", r.AllocationMode);
        Assert.True(r.IsActive);
    }

    [Fact]
    public async Task CreateResource_InvalidAllocationMode_Returns400()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "tool",
            Name = "Bad Tool",
            AllocationMode = "NotARealMode",
        };
        var response = await _client.PostAsJsonAsync("/api/resources", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_InvalidAvailabilityPercent_Returns400()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = "Over Person",
            AllocationMode = "Fractional",
            BaseAvailabilityPercent = 150,
        };
        var response = await _client.PostAsJsonAsync("/api/resources", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetResource_ById_ReturnsResource()
    {
        var created = await CreatePersonAsync($"GetById-{Guid.NewGuid():N}"[..20]);
        var response = await _client.GetAsync($"/api/resources/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var r = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.Equal(created.Id, r!.Id);
    }

    [Fact]
    public async Task GetResources_FilterByType_ReturnsOnlyMatchingType()
    {
        await CreatePersonAsync($"FilterPerson-{Guid.NewGuid():N}"[..20]);

        var response = await _client.GetAsync("/api/resources?resourceTypeKey=person");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        var list = envelope.GetProperty("data").Deserialize<List<ResourceInfo>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(list);
        Assert.All(list, r => Assert.Equal("person", r.ResourceTypeKey));
    }

    [Fact]
    public async Task DeactivateResource_SetsIsActiveFalse()
    {
        var created = await CreatePersonAsync($"Deactivate-{Guid.NewGuid():N}"[..20]);

        var deleteResponse = await _client.DeleteAsync($"/api/resources/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/resources/{created.Id}");
        var r = await getResponse.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.False(r!.IsActive);
    }

    [Fact]
    public async Task CreateResource_Unauthenticated_Returns401()
    {
        var anon = _fixture.Factory.CreateClient();
        anon.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TestConstants.TenantSlug);

        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = "Unauthorized",
            AllocationMode = "Fractional",
        };
        var response = await anon.PostAsJsonAsync("/api/resources", request);
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401/403, got {response.StatusCode}");
    }

    // ── Placeable resources ───────────────────────────────────────────────────

    private static async Task<List<ResourceInfo>> ReadListAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        return envelope.GetProperty("data").Deserialize<List<ResourceInfo>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>
    /// Creates a placeable resource the way the space route does — the defaults it hardcodes are
    /// supplied by the caller here, which is the point of the migration.
    /// </summary>
    private async Task<ResourceInfo> CreatePlaceableAsync(Guid siteId, string name, ResourceGeometry? geometry = null)
    {
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "space",
            Name = name,
            AllocationMode = "Exclusive",
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            IsPhysical = geometry is not null,
            Geometry = geometry,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    [Fact]
    public async Task GetResources_HasGeometry_ReturnsPlaceableAndExcludesPeople()
    {
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var placeable = await CreatePlaceableAsync(siteId, $"Placeable-{Guid.NewGuid():N}"[..20]);
        var person = await CreatePersonAsync($"NotPlaceable-{Guid.NewGuid():N}"[..20]);

        var list = await ReadListAsync(await _client.GetAsync("/api/resources?hasGeometry=true"));

        Assert.Contains(list, r => r.Id == placeable.Id);
        Assert.DoesNotContain(list, r => r.Id == person.Id);
    }

    [Fact]
    public async Task GetResources_HasGeometryFalse_ExcludesPlaceable()
    {
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var placeable = await CreatePlaceableAsync(siteId, $"OnlyPlaceable-{Guid.NewGuid():N}"[..20]);

        var list = await ReadListAsync(await _client.GetAsync("/api/resources?hasGeometry=false"));

        Assert.DoesNotContain(list, r => r.Id == placeable.Id);
    }

    [Fact]
    public async Task GetResources_HasGeometryWithSite_ScopesToTheSitesOwnPlaceableResources()
    {
        // The property the retired space route's site scoping relied on, kept under test now that
        // the route is gone. `siteId` on the generic list is the wider "home OR current site"
        // predicate; it selects exactly the site's own placeable rows only because a placeable
        // resource is created cross_site_allowed = false and so never has a different current
        // site. If that ever stops holding, this fails rather than the floorplan quietly gaining
        // another site's rows.
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var mine = await CreatePlaceableAsync(siteId, $"Mine-{Guid.NewGuid():N}"[..20]);

        var otherSiteResp = await _client.PostAsJsonAsync(
            "/api/sites", new CreateSiteRequest($"OS{Guid.NewGuid():N}"[..8], "Other Site", null, null));
        otherSiteResp.EnsureSuccessStatusCode();
        var otherSiteId = (await otherSiteResp.Content.ReadFromJsonAsync<SiteInfo>())!.Id;
        var theirs = await CreatePlaceableAsync(otherSiteId, $"Theirs-{Guid.NewGuid():N}"[..20]);

        var list = await ReadListAsync(
            await _client.GetAsync($"/api/resources?hasGeometry=true&isActive=true&siteId={siteId}"));

        Assert.Contains(list, r => r.Id == mine.Id);
        Assert.DoesNotContain(list, r => r.Id == theirs.Id);
    }

    [Fact]
    public async Task CreateResource_PhysicalWithoutGeometry_Returns400()
    {
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "space",
            Name = "Physical without shape",
            AllocationMode = "Exclusive",
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            IsPhysical = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_InvalidGeometry_Returns400()
    {
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        // A rectangle is exactly two points; one corner cannot describe a shape.
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "space",
            Name = "Half a rectangle",
            AllocationMode = "Exclusive",
            HomeSiteId = siteId,
            CrossSiteAllowed = false,
            IsPhysical = true,
            Geometry = new ResourceGeometry
            {
                Type = "rectangle",
                Coordinates = [new Coordinate { X = 0, Y = 0 }],
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateResource_ZeroCapacity_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = "No capacity",
            AllocationMode = "Fractional",
            Capacity = 0,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Clearing a field ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateResource_PresentNullHomeSite_UnsetsIt()
    {
        // The bug this fixes: "Unset" in the person dialog sent homeSiteId: null, the request
        // could not tell that from "not editing", and the column was silently left alone. The
        // save reported success while changing nothing.
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var person = await CreatePersonAsync($"HomeSite-{Guid.NewGuid():N}"[..20]);

        await _client.PutAsJsonAsync($"/api/resources/{person.Id}",
            new UpdateResourceRequest { HomeSiteId = Optional<Guid?>.Of(siteId) });
        var placed = await (await _client.GetAsync($"/api/resources/{person.Id}"))
            .Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.Equal(siteId, placed!.HomeSiteId);

        await _client.PutAsJsonAsync($"/api/resources/{person.Id}",
            new UpdateResourceRequest { HomeSiteId = Optional<Guid?>.Of(null) });

        var cleared = await (await _client.GetAsync($"/api/resources/{person.Id}"))
            .Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.Null(cleared!.HomeSiteId);
    }

    [Fact]
    public async Task UpdateResource_AbsentHomeSite_LeavesItAlone()
    {
        // The other half of the distinction: a rename must not wipe the site it never mentioned.
        var siteId = await TestHelpers.GetOrCreateTestSite(_client);
        var person = await CreatePersonAsync($"Untouched-{Guid.NewGuid():N}"[..20]);
        await _client.PutAsJsonAsync($"/api/resources/{person.Id}",
            new UpdateResourceRequest { HomeSiteId = Optional<Guid?>.Of(siteId) });

        await _client.PutAsJsonAsync($"/api/resources/{person.Id}",
            new UpdateResourceRequest { Name = "Renamed, nothing else" });

        var after = await (await _client.GetAsync($"/api/resources/{person.Id}"))
            .Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.Equal("Renamed, nothing else", after!.Name);
        Assert.Equal(siteId, after.HomeSiteId);
    }

    // ── Capabilities ──────────────────────────────────────────────────────────

    private async Task<CriterionInfo> GetSeedCriterionAsync(string name)
    {
        var response = await _client.GetAsync("/api/criteria");
        response.EnsureSuccessStatusCode();
        var criteria = await response.Content.ReadFromJsonAsync<List<CriterionInfo>>();
        return criteria!.First(c => c.Name == name);
    }

    [Fact]
    public async Task GetResourceCapabilities_ReturnsEmptyList_WhenNoCapabilities()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);

        // Act
        var response = await _client.GetAsync($"/api/resources/{resource.Id}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = await response.Content.ReadFromJsonAsync<List<ResourceCapabilityInfo>>();
        Assert.NotNull(capabilities);
        Assert.Empty(capabilities);
    }

    [Fact]
    public async Task GetResourceCapabilities_Returns404_WhenResourceNotFound()
    {
        // Arrange
        var nonExistentResourceId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/resources/{nonExistentResourceId}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddResourceCapability_CreatesCapability_WithValidData()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var criterion = await GetSeedCriterionAsync("seed_number");

        var request = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement(100.5));

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var capability = await response.Content.ReadFromJsonAsync<ResourceCapabilityInfo>();
        Assert.NotNull(capability);
        Assert.Equal(resource.Id, capability.ResourceId);
        Assert.Equal(criterion.Id, capability.CriterionId);
        Assert.Equal(100.5, capability.Value.GetDouble());
    }

    [Fact]
    public async Task AddResourceCapability_Returns400_WhenCriterionNotApplicableToResourceType()
    {
        // Spec acceptance §06: cross-type assignment must be rejected server-side.
        // Create a criterion tagged for space only, then try to assign it to a person resource.
        var create = new CreateCriterionRequest
        {
            Name = $"space_only_cross_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" },
        };
        var createResp = await _client.PostAsJsonAsync("/api/criteria", create);
        createResp.EnsureSuccessStatusCode();
        var criterion = (await createResp.Content.ReadFromJsonAsync<CriterionInfo>())!;

        var applicability = new UpdateCriterionApplicabilityRequest
        {
            ResourceTypeKeys = new List<string> { "space" },
        };
        var applyResp = await _client.PutAsJsonAsync(
            $"/api/criteria/{criterion.Id}/applicability", applicability);
        applyResp.EnsureSuccessStatusCode();

        var person = await CreatePersonAsync("CrossType-" + Guid.NewGuid().ToString("N")[..16]);
        var request = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement(true));

        var response = await _client.PostAsJsonAsync(
            $"/api/resources/{person.Id}/capabilities", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddResourceCapability_Returns404_WhenResourceNotFound()
    {
        // Arrange
        var nonExistentResourceId = Guid.NewGuid();
        var criterion = await GetSeedCriterionAsync("seed_boolean");

        var request = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement(true));

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/resources/{nonExistentResourceId}/capabilities",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteResourceCapability_RemovesCapability_WhenExists()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var criterion = await GetSeedCriterionAsync("seed_string");

        // Create capability first
        var createRequest = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement("test-value"));
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/capabilities",
            createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ResourceCapabilityInfo>();
        Assert.NotNull(created);

        // Act
        var response = await _client.DeleteAsync(
            $"/api/resources/{resource.Id}/capabilities/{created.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's gone
        var getResponse = await _client.GetAsync($"/api/resources/{resource.Id}/capabilities");
        var capabilities = await getResponse.Content.ReadFromJsonAsync<List<ResourceCapabilityInfo>>();
        Assert.NotNull(capabilities);
        Assert.Empty(capabilities);
    }

    [Fact]
    public async Task DeleteResourceCapability_Returns404_WhenCapabilityNotFound()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var nonExistentCapabilityId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync(
            $"/api/resources/{resource.Id}/capabilities/{nonExistentCapabilityId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddResourceCapability_ThenGetReturnsIt()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var criterion = await GetSeedCriterionAsync("seed_number");

        var request = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement(42.5));
        await _client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/capabilities",
            request);

        // Act
        var response = await _client.GetAsync($"/api/resources/{resource.Id}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = await response.Content.ReadFromJsonAsync<List<ResourceCapabilityInfo>>();
        Assert.NotNull(capabilities);
        Assert.Single(capabilities);
        var capability = capabilities[0];
        Assert.Equal(criterion.Id, capability.CriterionId);
        Assert.Equal(42.5, capability.Value.GetDouble());
    }

    [Fact]
    public async Task GetResourceCapabilities_ReturnsCapabilitiesWithCriterionDetails()
    {
        // Arrange
        var resource = await CreatePersonAsync("CapTest-" + Guid.NewGuid().ToString("N")[..20]);
        var criterion = await GetSeedCriterionAsync("seed_number");

        var request = new AddResourceCapabilityRequest(criterion.Id, JsonSerializer.SerializeToElement(100.5));
        await _client.PostAsJsonAsync(
            $"/api/resources/{resource.Id}/capabilities",
            request);

        // Act
        var response = await _client.GetAsync($"/api/resources/{resource.Id}/capabilities");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = await response.Content.ReadFromJsonAsync<List<ResourceCapabilityInfo>>();
        Assert.NotNull(capabilities);
        Assert.Single(capabilities);

        var capability = capabilities[0];
        Assert.NotNull(capability.Criterion);
        Assert.Equal(criterion.Name, capability.Criterion.Name);
        Assert.Equal(CriterionDataType.Number, capability.Criterion.DataType);
    }

    [Fact]
    public async Task GetResource_Person_CarriesDirectoryFields_WithNotesDecrypted()
    {
        // D2: the directory details a person carries are part of the generic resource contract,
        // not a separate document to fetch. The fields live on `resources` (migration 1700), so
        // this reads them from the same row rather than joining anything.
        var person = await CreatePersonAsync($"Dir-{Guid.NewGuid():N}"[..20]);
        var email = $"dir_{Guid.NewGuid():N}@example.com";

        var upsert = await _client.PutAsJsonAsync($"/api/person-profiles/{person.Id}",
            new UpsertPersonProfileRequest { Email = email, Notes = "Confidential note" });
        Assert.Equal(HttpStatusCode.OK, upsert.StatusCode);

        var fetched = await _client.GetFromJsonAsync<ResourceInfo>($"/api/resources/{person.Id}");

        Assert.Equal(email, fetched!.Email);
        // Notes are encrypted at rest, so a read path that forgot to decrypt would return
        // ciphertext rather than fail — which is why this asserts the plaintext, not just non-null.
        Assert.Equal("Confidential note", fetched.Notes);
    }

    [Fact]
    public async Task GetResource_NonPerson_HasNoDirectoryFields()
    {
        // A type that declares no directory profile simply has nulls there: the columns exist for
        // every resource, and nothing populates them.
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "tool",
            Name = $"Tool-{Guid.NewGuid():N}"[..20],
            AllocationMode = "Exclusive",
        };
        var created = await _client.PostAsJsonAsync("/api/resources", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tool = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;

        Assert.Null(tool.Email);
        Assert.Null(tool.Notes);
    }

    [Fact]
    public async Task CreateResource_Person_WritesDirectoryFields_AndStoresNotesEncrypted()
    {
        var email = $"dir_{Guid.NewGuid():N}@example.com";
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = $"Dir-{Guid.NewGuid():N}"[..20],
            AllocationMode = "Fractional",
            Email = email,
            Notes = "Salary review due",
        };

        var created = await _client.PostAsJsonAsync("/api/resources", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var person = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;

        Assert.Equal(email, person.Email);
        Assert.Equal("Salary review due", person.Notes);

        // Round-tripping plaintext proves the pair of transforms agree, not that anything is
        // encrypted — an implementation that stored plaintext would pass that too. So read the
        // column directly: what is at rest must not be what the caller sent.
        var tenantConnectionString =
            $"Host=localhost;Port={_fixture.DatabasePort};Database={TestConstants.TenantDatabase};"
            + "Username=postgres;Password=postgres";
        await using var db = new NpgsqlConnection(tenantConnectionString);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT notes FROM resources WHERE id = @id";
        cmd.Parameters.AddWithValue("id", person.Id);
        var stored = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(stored);
        Assert.NotEqual("Salary review due", stored);
    }

    [Fact]
    public async Task UpdateResource_RejectsDirectoryFieldsOnATypeWithoutADirectoryProfile()
    {
        // The mirror of the placement rule: a type that declares no directory profile has no
        // email or notes, and a request carrying them half-means something else.
        var created = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "tool",
            Name = $"Tool-{Guid.NewGuid():N}"[..20],
            AllocationMode = "Exclusive",
        });
        var tool = (await created.Content.ReadFromJsonAsync<ResourceInfo>())!;

        var response = await _client.PutAsJsonAsync($"/api/resources/{tool.Id}",
            new UpdateResourceRequest { Notes = "not allowed here" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
