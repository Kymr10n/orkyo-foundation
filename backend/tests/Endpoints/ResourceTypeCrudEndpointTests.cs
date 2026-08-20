using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// User-defined resource types: lifecycle, keys, icons, and the protections on system types.
/// The fixture database is shared across the suite, so every test mints its own type key.
/// Attribute definitions live on criteria — see ResourceCapabilityValueTests.
/// </summary>
[Collection("Database collection")]
public class ResourceTypeCrudEndpointTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;

    public ResourceTypeCrudEndpointTests(DatabaseFixture databaseFixture)
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
            DisplayNamePlural = "Cars",
            Description = "Fleet vehicle",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;
    }



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
            DisplayNamePlural = "Duplicates",
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
            DisplayNamePlural = "Bad keys",
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
    public async Task CreateResourceType_RoundTripsIcon()
    {
        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("van"),
            DisplayName = "Van",
            DisplayNamePlural = "Vans",
            Icon = "Truck",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ResourceTypeInfo>();
        Assert.Equal("Truck", created!.Icon);
    }

    [Fact]
    public async Task CreateResourceType_LeavesIconNull_WhenOmitted()
    {
        var created = await CreateTypeAsync();

        Assert.Null(created.Icon);
    }

    [Fact]
    public async Task UpdateResourceType_ChangesIcon()
    {
        var created = await CreateTypeAsync();

        var response = await _client.PutAsJsonAsync($"/api/resource-types/{created.Id}",
            new UpdateResourceTypeRequest { Icon = "Car" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ResourceTypeInfo>();
        Assert.Equal("Car", updated!.Icon);
        // Icon-only updates must not disturb the rest of the row.
        Assert.Equal(created.DisplayName, updated.DisplayName);
    }

    /// <summary>
    /// Icons are a frontend concern — the server stores the name verbatim and does not police
    /// the allow-list, so an unknown name is accepted here and degrades to a default in the UI.
    /// </summary>
    [Fact]
    public async Task CreateResourceType_Rejects_IconOverMaxLength()
    {
        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("bus"),
            DisplayName = "Bus",
            DisplayNamePlural = "Buss",
            Icon = new string('x', 51),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SystemTypes_HaveSeededIcons()
    {
        var types = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");

        Assert.Equal("Box", Assert.Single(types!, t => t.Key == "space").Icon);
        Assert.Equal("Users", Assert.Single(types!, t => t.Key == "person").Icon);
        Assert.Equal("Wrench", Assert.Single(types!, t => t.Key == "tool").Icon);
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
    public async Task FormerlyBuiltInTypes_CanBeDeactivatedAndReactivated()
    {
        // Nothing is built in any more: person is an ordinary tenant type, so its lifecycle is
        // the tenant's — this is what the catalog's off-switch relies on. Restored at the end
        // because the rest of the suite creates person resources.
        var all = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");
        var person = all!.First(t => t.Key == ResourceTypeKeys.Person);

        var deactivate = await _client.PutAsJsonAsync($"/api/resource-types/{person.Id}",
            new UpdateResourceTypeRequest { IsActive = false });
        deactivate.EnsureSuccessStatusCode();
        (await deactivate.Content.ReadFromJsonAsync<ResourceTypeInfo>())!.IsActive.Should().BeFalse();

        var reactivate = await _client.PutAsJsonAsync($"/api/resource-types/{person.Id}",
            new UpdateResourceTypeRequest { IsActive = true });
        reactivate.EnsureSuccessStatusCode();
        (await reactivate.Content.ReadFromJsonAsync<ResourceTypeInfo>())!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task NoTypeIsSystem_EveryRowIsAnOrdinaryTenantType()
    {
        // 1800 demoted space, 1870 demoted the rest, and the API has never been able to create
        // a system type — so is_system is false everywhere, always.
        var all = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");

        Assert.All(all!, t => Assert.False(t.IsSystem));
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
            DisplayNamePlural = "Nopes",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditorCannotWriteResourceTypes()
    {
        // Defining the catalogue is governance, not content editing — an editor who can
        // create resources all day still cannot invent a new kind of them.
        var editor = _fixture.CreateClientWithRole("editor");
        var existing = await CreateTypeAsync();

        var created = await editor.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("editor"),
            DisplayName = "Nope",
            DisplayNamePlural = "Nopes",
        });
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);

        var updated = await editor.PutAsJsonAsync($"/api/resource-types/{existing.Id}",
            new UpdateResourceTypeRequest { DisplayName = "Hijacked" });
        Assert.Equal(HttpStatusCode.Forbidden, updated.StatusCode);

        var deleted = await editor.DeleteAsync($"/api/resource-types/{existing.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
    }

    [Fact]
    public async Task EditorCanReadResourceTypes()
    {
        // Reads stay member-open: every resource page and form needs the type list.
        await CreateTypeAsync();
        var editor = _fixture.CreateClientWithRole("editor");

        var response = await editor.GetAsync("/api/resource-types");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var types = (await response.Content.ReadFromJsonAsync<List<ResourceTypeInfo>>())!;
        Assert.NotEmpty(types);
    }

    // ── field definitions ─────────────────────────────────────────────────────

    // ── Behaviour flags ───────────────────────────────────────────────────────
    // Migration 1700 moved three behaviours out of key comparisons and onto the type. These
    // cover the part that makes that worth doing: a tenant type can declare them.

    [Fact]
    public async Task CreateType_DeclaringBehaviour_RoundTripsTheFlags()
    {
        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("bay"),
            DisplayName = "Bay",
            DisplayNamePlural = "Bays",
            HasGeometry = true,
            SingleGroupMembership = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ResourceTypeInfo>();
        created!.HasGeometry.Should().BeTrue();
        created.SingleGroupMembership.Should().BeTrue();
        created.HasDirectoryProfile.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateType_CanChangeBehaviour_OnATenantType()
    {
        var type = await CreateTypeAsync();

        var response = await _client.PutAsJsonAsync($"/api/resource-types/{type.Id}",
            new UpdateResourceTypeRequest { HasDirectoryProfile = true });
        response.EnsureSuccessStatusCode();

        (await response.Content.ReadFromJsonAsync<ResourceTypeInfo>())!
            .HasDirectoryProfile.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateType_FormerlyBuiltIn_BehaviourAndNamingAreEditable()
    {
        var types = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");
        var person = types!.Single(t => t.Key == ResourceTypeKeys.Person);

        // Behaviour flags used to be locked on system types; nothing is system any more, so
        // the tenant owns them. Flipped and restored, because the directory suite depends on
        // person's flag.
        var behaviour = await _client.PutAsJsonAsync($"/api/resource-types/{person.Id}",
            new UpdateResourceTypeRequest { HasDirectoryProfile = false });
        behaviour.EnsureSuccessStatusCode();
        (await behaviour.Content.ReadFromJsonAsync<ResourceTypeInfo>())!.HasDirectoryProfile
            .Should().BeFalse();
        var restoreFlag = await _client.PutAsJsonAsync($"/api/resource-types/{person.Id}",
            new UpdateResourceTypeRequest { HasDirectoryProfile = true });
        restoreFlag.EnsureSuccessStatusCode();

        // Naming was always theirs — "Person" may be "Colleague" in their vocabulary.
        var rename = await _client.PutAsJsonAsync($"/api/resource-types/{person.Id}",
            new UpdateResourceTypeRequest { DisplayName = "Colleague", DisplayNamePlural = "Colleagues" });
        rename.EnsureSuccessStatusCode();
        (await rename.Content.ReadFromJsonAsync<ResourceTypeInfo>())!.DisplayNamePlural
            .Should().Be("Colleagues");

        // Restore, so the rest of the suite sees the classic vocabulary.
        await _client.PutAsJsonAsync($"/api/resource-types/{person.Id}",
            new UpdateResourceTypeRequest { DisplayName = "Person", DisplayNamePlural = "People" });
    }

    [Fact]
    public async Task DeleteType_NamedByARequestTarget_DeactivatesInsteadOfFailing()
    {
        // request_target_resource_types holds the type via ON DELETE RESTRICT. Without the
        // service-side check the user meets a raw 23503 instead of the retire-instead path.
        var type = await CreateTypeAsync();
        var request = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"NeedsType-{Guid.NewGuid():N}"[..25],
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            TargetResourceTypeKeys = [type.Key],
        });
        request.EnsureSuccessStatusCode();

        var deleted = await _client.DeleteAsync($"/api/resource-types/{type.Id}");
        deleted.IsSuccessStatusCode.Should().BeTrue();

        var after = await _client.GetFromJsonAsync<List<ResourceTypeInfo>>("/api/resource-types");
        after!.Single(t => t.Id == type.Id).IsActive.Should().BeFalse();
    }


    [Fact]
    public async Task PlaceableResource_CannotBeCreatedAsTravelling()
    {
        // A drawn shape belongs to one floorplan. Site resolution and the working-hours
        // adjustment both look for the first resource that cannot travel to decide where the
        // work happens, and would skip a placeable one that claimed it could.
        var type = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = UniqueKey("bay"),
            DisplayName = "Bay",
            DisplayNamePlural = "Bays",
            HasGeometry = true,
        });
        type.EnsureSuccessStatusCode();
        var placeable = (await type.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;

        var resp = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = placeable.Key,
            Name = $"Bay-{Guid.NewGuid():N}"[..20],
            AllocationMode = AllocationModes.Exclusive,
            BaseAvailabilityPercent = 100,
            CrossSiteAllowed = true,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

}
