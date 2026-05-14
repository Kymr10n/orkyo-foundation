using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

[Collection("Database collection")]
public class ResourceAbsenceEndpointTests
{
    private readonly HttpClient _client;

    public ResourceAbsenceEndpointTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
    }

    private async Task<Guid> GetOrCreateSiteIdAsync()
    {
        var resp = await _client.GetAsync("/api/sites");
        var sites = await resp.Content.ReadFromJsonAsync<List<SiteInfo>>();
        if (sites is { Count: > 0 }) return sites[0].Id;

        var create = await _client.PostAsJsonAsync("/api/sites", new { Code = $"abs-{Guid.NewGuid():N}"[..20], Name = "Absence Test Site" });
        return (await create.Content.ReadFromJsonAsync<SiteInfo>())!.Id;
    }

    private async Task<ResourceInfo> CreatePersonAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = name,
            AllocationMode = "Fractional",
            BaseAvailabilityPercent = 100,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    [Fact]
    public async Task CreateAbsence_Returns201WithOffTimeLinkedToResource()
    {
        var siteId = await GetOrCreateSiteIdAsync();
        var person = await CreatePersonAsync($"AbsPerson-{Guid.NewGuid():N}"[..20]);

        var start = DateTime.UtcNow.Date.AddDays(1);
        var resp = await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest
            {
                SiteId = siteId,
                Title = "Vacation",
                Type = OffTimeType.Custom,
                StartTs = start,
                EndTs = start.AddDays(5),
            });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var absence = await resp.Content.ReadFromJsonAsync<OffTimeInfo>();
        Assert.NotNull(absence);
        Assert.Equal("Vacation", absence.Title);
        Assert.False(absence.AppliesToAllResources);
        Assert.Contains(person.Id, absence.ResourceIds!);
    }

    [Fact]
    public async Task GetAbsences_ReturnsCreatedAbsence()
    {
        var siteId = await GetOrCreateSiteIdAsync();
        var person = await CreatePersonAsync($"AbsGet-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.Date.AddDays(2);

        await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest
            {
                SiteId = siteId,
                Title = "Sick Leave",
                StartTs = start,
                EndTs = start.AddDays(3),
            });

        var resp = await _client.GetAsync($"/api/resources/{person.Id}/absences?siteId={siteId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<List<OffTimeInfo>>();
        Assert.NotNull(list);
        Assert.Contains(list, a => a.Title == "Sick Leave" && a.ResourceIds!.Contains(person.Id));
    }

    [Fact]
    public async Task UpdateAbsence_ChangesTitle()
    {
        var siteId = await GetOrCreateSiteIdAsync();
        var person = await CreatePersonAsync($"AbsUpd-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.Date.AddDays(10);

        var created = await (await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest { SiteId = siteId, Title = "Original", StartTs = start, EndTs = start.AddDays(1) }))
            .Content.ReadFromJsonAsync<OffTimeInfo>();

        var upd = await _client.PutAsJsonAsync(
            $"/api/resources/{person.Id}/absences/{created!.Id}",
            new UpdateResourceAbsenceRequest { Title = "Updated" });
        Assert.Equal(HttpStatusCode.OK, upd.StatusCode);
        var updated = await upd.Content.ReadFromJsonAsync<OffTimeInfo>();
        Assert.Equal("Updated", updated!.Title);
    }

    [Fact]
    public async Task DeleteAbsence_Returns204()
    {
        var siteId = await GetOrCreateSiteIdAsync();
        var person = await CreatePersonAsync($"AbsDel-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.Date.AddDays(20);

        var created = await (await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest { SiteId = siteId, Title = "ToDelete", StartTs = start, EndTs = start.AddDays(1) }))
            .Content.ReadFromJsonAsync<OffTimeInfo>();

        var del = await _client.DeleteAsync($"/api/resources/{person.Id}/absences/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // Verify it's gone
        var list = await (await _client.GetAsync($"/api/resources/{person.Id}/absences?siteId={siteId}"))
            .Content.ReadFromJsonAsync<List<OffTimeInfo>>();
        Assert.DoesNotContain(list!, a => a.Id == created.Id);
    }

    [Fact]
    public async Task UpdateAbsence_WrongResource_Returns404()
    {
        var siteId = await GetOrCreateSiteIdAsync();
        var person = await CreatePersonAsync($"AbsWrong-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.Date.AddDays(30);

        var created = await (await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest { SiteId = siteId, Title = "Mine", StartTs = start, EndTs = start.AddDays(1) }))
            .Content.ReadFromJsonAsync<OffTimeInfo>();

        var otherId = Guid.NewGuid();
        var resp = await _client.PutAsJsonAsync(
            $"/api/resources/{otherId}/absences/{created!.Id}",
            new UpdateResourceAbsenceRequest { Title = "Hijack" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
