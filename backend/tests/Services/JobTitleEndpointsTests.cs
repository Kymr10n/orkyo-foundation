using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

[Collection("Database collection")]
public class JobTitleEndpointsTests
{
    private readonly HttpClient _client;

    public JobTitleEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
    }

    [Fact]
    public async Task Create_ThenGet_RoundTrips()
    {
        var name = $"JT-{Guid.NewGuid():N}"[..20];
        var createResp = await _client.PostAsJsonAsync("/api/job-titles",
            new CreateJobTitleRequest { Name = name, Description = "test" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var created = await createResp.Content.ReadFromJsonAsync<JobTitleInfo>();
        Assert.NotNull(created);
        Assert.Equal(name, created.Name);
        Assert.Equal("test", created.Description);
        Assert.True(created.IsActive);

        var getResp = await _client.GetAsync($"/api/job-titles/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        var name = $"JT-DUP-{Guid.NewGuid():N}"[..20];
        var first = await _client.PostAsJsonAsync("/api/job-titles",
            new CreateJobTitleRequest { Name = name });
        first.EnsureSuccessStatusCode();

        var second = await _client.PostAsJsonAsync("/api/job-titles",
            new CreateJobTitleRequest { Name = name });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Update_TogglesIsActive()
    {
        var name = $"JT-INA-{Guid.NewGuid():N}"[..20];
        var created = await CreateAsync(name);

        var deactResp = await _client.PutAsJsonAsync($"/api/job-titles/{created.Id}",
            new UpdateJobTitleRequest { IsActive = false });
        deactResp.EnsureSuccessStatusCode();
        var updated = await deactResp.Content.ReadFromJsonAsync<JobTitleInfo>();
        Assert.NotNull(updated);
        Assert.False(updated.IsActive);

        // Default GET excludes inactive — confirm absent
        var listResp = await _client.GetAsync("/api/job-titles");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<JobTitleInfo>>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list, x => x.Id == created.Id);

        // includeInactive=true brings it back
        var allResp = await _client.GetAsync("/api/job-titles?includeInactive=true");
        var all = await allResp.Content.ReadFromJsonAsync<List<JobTitleInfo>>();
        Assert.NotNull(all);
        Assert.Contains(all, x => x.Id == created.Id);
    }

    private async Task<JobTitleInfo> CreateAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/job-titles",
            new CreateJobTitleRequest { Name = name });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JobTitleInfo>())!;
    }
}
