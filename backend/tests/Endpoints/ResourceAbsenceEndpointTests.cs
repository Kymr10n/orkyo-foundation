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
    public async Task CreateAbsence_Returns201WithResourceAbsence()
    {
        var person = await CreatePersonAsync($"AbsPerson-{Guid.NewGuid():N}"[..20]);

        var start = DateTime.UtcNow.Date.AddDays(1);
        var resp = await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest
            {
                AbsenceType = AbsenceType.Vacation,
                Title = "Summer holiday",
                StartTs = start,
                EndTs = start.AddDays(5),
            });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var absence = await resp.Content.ReadFromJsonAsync<ResourceAbsenceInfo>();
        Assert.NotNull(absence);
        Assert.Equal("Summer holiday", absence.Title);
        Assert.Equal(AbsenceType.Vacation, absence.AbsenceType);
        Assert.Equal(person.Id, absence.ResourceId);
    }

    [Fact]
    public async Task GetAbsences_ReturnsCreatedAbsence()
    {
        var person = await CreatePersonAsync($"AbsGet-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.Date.AddDays(2);

        await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest
            {
                AbsenceType = AbsenceType.Sickness,
                Title = "Sick Leave",
                StartTs = start,
                EndTs = start.AddDays(3),
            });

        var resp = await _client.GetAsync($"/api/resources/{person.Id}/absences");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<List<ResourceAbsenceInfo>>();
        Assert.NotNull(list);
        Assert.Contains(list, a => a.Title == "Sick Leave" && a.ResourceId == person.Id);
    }

    [Fact]
    public async Task UpdateAbsence_ChangesTitle()
    {
        var person = await CreatePersonAsync($"AbsUpd-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.Date.AddDays(10);

        var created = await (await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest
            {
                AbsenceType = AbsenceType.Custom,
                Title = "Original",
                StartTs = start,
                EndTs = start.AddDays(1)
            }))
            .Content.ReadFromJsonAsync<ResourceAbsenceInfo>();

        var upd = await _client.PutAsJsonAsync(
            $"/api/resources/{person.Id}/absences/{created!.Id}",
            new UpdateResourceAbsenceRequest { Title = "Updated" });
        Assert.Equal(HttpStatusCode.OK, upd.StatusCode);
        var updated = await upd.Content.ReadFromJsonAsync<ResourceAbsenceInfo>();
        Assert.Equal("Updated", updated!.Title);
    }

    [Fact]
    public async Task DeleteAbsence_Returns204()
    {
        var person = await CreatePersonAsync($"AbsDel-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.Date.AddDays(20);

        var created = await (await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest
            {
                AbsenceType = AbsenceType.Training,
                Title = "ToDelete",
                StartTs = start,
                EndTs = start.AddDays(1)
            }))
            .Content.ReadFromJsonAsync<ResourceAbsenceInfo>();

        var del = await _client.DeleteAsync($"/api/resources/{person.Id}/absences/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await (await _client.GetAsync($"/api/resources/{person.Id}/absences"))
            .Content.ReadFromJsonAsync<List<ResourceAbsenceInfo>>();
        Assert.DoesNotContain(list!, a => a.Id == created.Id);
    }

    [Fact]
    public async Task UpdateAbsence_WrongResource_Returns404()
    {
        var person = await CreatePersonAsync($"AbsWrong-{Guid.NewGuid():N}"[..20]);
        var start = DateTime.UtcNow.Date.AddDays(30);

        var created = await (await _client.PostAsJsonAsync($"/api/resources/{person.Id}/absences",
            new CreateResourceAbsenceRequest
            {
                AbsenceType = AbsenceType.Unavailable,
                Title = "Mine",
                StartTs = start,
                EndTs = start.AddDays(1)
            }))
            .Content.ReadFromJsonAsync<ResourceAbsenceInfo>();

        var otherId = Guid.NewGuid();
        var resp = await _client.PutAsJsonAsync(
            $"/api/resources/{otherId}/absences/{created!.Id}",
            new UpdateResourceAbsenceRequest { Title = "Hijack" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
