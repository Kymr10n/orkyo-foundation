using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ResourceScanCodeEndpointTests
{
    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _client;

    public ResourceScanCodeEndpointTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateAuthorizedClient();
    }

    private static string UniqueCode() => $"https://vendor.example/asset?id={Guid.NewGuid():N}#tag";

    private async Task<ResourceTypeInfo> CreateTypeAsync(bool scanCodesEnabled = true)
    {
        var response = await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = $"tagged_{Guid.NewGuid():N}",
            DisplayName = "Tagged",
            DisplayNamePlural = "Tagged",
            ScanCodesEnabled = scanCodesEnabled,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceTypeInfo>())!;
    }

    private async Task<ResourceInfo> CreateResourceAsync(ResourceTypeInfo type, string? name = null)
    {
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = type.Key,
            Name = name ?? $"Drill {Guid.NewGuid():N}"[..20],
            AllocationMode = "Exclusive",
            BaseAvailabilityPercent = 100,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    private Task<HttpResponseMessage> LinkAsync(Guid resourceId, string code, bool move = false, HttpClient? client = null)
        => (client ?? _client).PostAsJsonAsync($"/api/resources/{resourceId}/scan-codes",
            new LinkResourceScanCodeRequest { Code = code, MoveFromOtherResource = move });

    private async Task<ResourceScanCodeInfo> LinkAndReadAsync(Guid resourceId, string code, bool move = false)
    {
        var response = await LinkAsync(resourceId, code, move);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResourceScanCodeInfo>())!;
    }

    private Task<List<ResourceScanCodeInfo>?> ListCodesAsync(Guid resourceId)
        => _client.GetFromJsonAsync<List<ResourceScanCodeInfo>>($"/api/resources/{resourceId}/scan-codes");

    private async Task<ScanCodeLookupResult> LookupAsync(string code, HttpClient? client = null)
    {
        var response = await (client ?? _client).GetAsync(
            $"/api/resources/scan-codes/lookup?code={Uri.EscapeDataString(code)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ScanCodeLookupResult>())!;
    }

    [Fact]
    public async Task Link_ThenLookup_FindsTheResource()
    {
        var type = await CreateTypeAsync();
        var drill = await CreateResourceAsync(type);
        var code = UniqueCode();

        var info = await LinkAndReadAsync(drill.Id, $"  {code}\n");
        Assert.Equal(code, info.Code);

        var lookup = await LookupAsync(code);
        Assert.Equal(ScanCodeLookupStatus.Linked, lookup.Status);
        Assert.Equal(drill.Id, lookup.Resource!.Id);
        Assert.Equal(type.Key, lookup.Resource.ResourceTypeKey);

        Assert.Equal(code, Assert.Single((await ListCodesAsync(drill.Id))!).Code);
    }

    [Fact]
    public async Task Link_SameCodeTwice_ReturnsTheExistingLink()
    {
        var drill = await CreateResourceAsync(await CreateTypeAsync());
        var code = UniqueCode();
        var first = await LinkAndReadAsync(drill.Id, code);

        Assert.Equal(first.Id, (await LinkAndReadAsync(drill.Id, code)).Id);
        Assert.Single((await ListCodesAsync(drill.Id))!);
    }

    [Fact]
    public async Task Link_CodeOfAnotherResource_Returns409UnlessMoved()
    {
        var type = await CreateTypeAsync();
        var first = await CreateResourceAsync(type, "First drill");
        var second = await CreateResourceAsync(type);
        var code = UniqueCode();
        await LinkAsync(first.Id, code);

        var refused = await LinkAsync(second.Id, code);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("First drill", await refused.Content.ReadAsStringAsync());
        Assert.Equal(first.Id, (await LookupAsync(code)).Resource!.Id);

        var moved = await LinkAsync(second.Id, code, move: true);
        Assert.Equal(HttpStatusCode.Created, moved.StatusCode);
        Assert.Equal(second.Id, (await LookupAsync(code)).Resource!.Id);
        Assert.Empty((await ListCodesAsync(first.Id))!);
    }

    [Fact]
    public async Task Link_TypeWithScanningOff_Returns400()
    {
        var drill = await CreateResourceAsync(await CreateTypeAsync(scanCodesEnabled: false));

        var response = await LinkAsync(drill.Id, UniqueCode());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_TypeSwitchedOffAfterLinking_DoesNotNameTheResource()
    {
        var type = await CreateTypeAsync();
        var drill = await CreateResourceAsync(type);
        var code = UniqueCode();
        await LinkAsync(drill.Id, code);

        await _client.PutAsJsonAsync($"/api/resource-types/{type.Id}", new UpdateResourceTypeRequest { ScanCodesEnabled = false });
        var lookup = await LookupAsync(code);

        Assert.Equal(ScanCodeLookupStatus.TypeDisabled, lookup.Status);
        Assert.Null(lookup.Resource);
    }

    [Fact]
    public async Task Lookup_DeactivatedResource_StillFindsIt()
    {
        var drill = await CreateResourceAsync(await CreateTypeAsync());
        var code = UniqueCode();
        await LinkAndReadAsync(drill.Id, code);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/resources/{drill.Id}")).StatusCode);

        var lookup = await LookupAsync(code);

        Assert.Equal(ScanCodeLookupStatus.Linked, lookup.Status);
        Assert.False(lookup.Resource!.IsActive);
    }

    [Fact]
    public async Task Lookup_UnknownCode_ReportsUnknown()
    {
        var lookup = await LookupAsync(UniqueCode());

        Assert.Equal(ScanCodeLookupStatus.Unknown, lookup.Status);
        Assert.Null(lookup.Resource);
    }

    [Fact]
    public async Task Lookup_WithoutCode_Returns400()
    {
        var response = await _client.GetAsync("/api/resources/scan-codes/lookup?code=%20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Link_EmptyOrOverlongCode_Returns400()
    {
        var drill = await CreateResourceAsync(await CreateTypeAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(drill.Id, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LinkAsync(drill.Id, new string('x', 513))).StatusCode);
    }

    [Fact]
    public async Task UnknownResource_Returns404()
    {
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/resources/{missing}/scan-codes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await LinkAsync(missing, UniqueCode())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.DeleteAsync($"/api/resources/{missing}/scan-codes/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Unlink_RemovesTheCode_AndOnlyFromItsOwnResource()
    {
        var type = await CreateTypeAsync();
        var drill = await CreateResourceAsync(type);
        var other = await CreateResourceAsync(type);
        var code = UniqueCode();
        var info = await LinkAndReadAsync(drill.Id, code);

        var wrongOwner = await _client.DeleteAsync($"/api/resources/{other.Id}/scan-codes/{info.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongOwner.StatusCode);

        var deleted = await _client.DeleteAsync($"/api/resources/{drill.Id}/scan-codes/{info.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(ScanCodeLookupStatus.Unknown, (await LookupAsync(code)).Status);
    }

    // ── authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Viewer_CanLookUpListAndReadStatus_ButNotLinkOrUnlink()
    {
        var drill = await CreateResourceAsync(await CreateTypeAsync());
        var code = UniqueCode();
        var info = await LinkAndReadAsync(drill.Id, code);
        var viewer = _fixture.CreateClientWithRole("viewer");

        Assert.Equal(drill.Id, (await LookupAsync(code, viewer)).Resource!.Id);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/resources/{drill.Id}/scan-codes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/resources/{drill.Id}/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await LinkAsync(drill.Id, UniqueCode(), client: viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.DeleteAsync($"/api/resources/{drill.Id}/scan-codes/{info.Id}")).StatusCode);
    }

    [Fact]
    public async Task Editor_CanLink()
    {
        var drill = await CreateResourceAsync(await CreateTypeAsync());

        var response = await LinkAsync(drill.Id, UniqueCode(), client: _fixture.CreateClientWithRole("editor"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── status ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_ReturnsTheResourceSummary()
    {
        var drill = await CreateResourceAsync(await CreateTypeAsync(), "Status drill");

        var status = await _client.GetFromJsonAsync<ResourceStatusInfo>($"/api/resources/{drill.Id}/status");

        Assert.Equal(drill.Id, status!.ResourceId);
        Assert.Equal("Status drill", status.Name);
        Assert.Null(status.Current);
        Assert.Null(status.Next);
        Assert.Equal(0, status.ConflictCount);
        Assert.Equal(30, status.LookAheadDays);
    }

    [Fact]
    public async Task Status_UnknownResource_Returns404()
    {
        var response = await _client.GetAsync($"/api/resources/{Guid.NewGuid()}/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
