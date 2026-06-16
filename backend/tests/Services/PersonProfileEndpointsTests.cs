using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Integration tests for Person Profile API endpoints.
/// Requires PersonProfileEndpoints to be registered in the application.
/// </summary>
[Collection("Database collection")]
public class PersonProfileEndpointsTests
{
    private readonly HttpClient _client;

    public PersonProfileEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
    }

    private async Task<ResourceInfo> CreatePersonAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "person",
            Name = name,
            AllocationMode = "Exclusive",
            BaseAvailabilityPercent = 100,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    private async Task<Guid> CreateSpaceIdAsync()
    {
        // Use the shared helper so we don't duplicate site discovery + space creation logic.
        return await TestHelpers.GetOrCreateTestSpace(_client);
    }

    // Note: These tests require MapPersonProfileEndpoints to be registered in the application.
    // They are placed here for documentation and will pass once the endpoints are wired.

    [Fact]
    public async Task GetPersonProfile_ResourceNotFound_Returns404()
    {
        var resp = await _client.GetAsync($"/api/person-profiles/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetPersonProfile_ResourceNotPerson_Returns400()
    {
        var spaceId = await CreateSpaceIdAsync();
        var resp = await _client.GetAsync($"/api/person-profiles/{spaceId}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UpsertPersonProfile_CreatesAndRetrievesProfile()
    {
        var person = await CreatePersonAsync($"P-{Guid.NewGuid():N}"[..20]);

        // Set up reference data: a job title and a 2-level department tree, so
        // we can prove that the upsert preserves the FKs and the GET resolves
        // department_path correctly.
        var jobTitleId = await CreateJobTitleAsync($"JT-{Guid.NewGuid():N}"[..20]);
        var rootDeptId = await CreateDepartmentAsync($"D-{Guid.NewGuid():N}"[..20], parentId: null);
        var childDeptId = await CreateDepartmentAsync($"D-{Guid.NewGuid():N}"[..20], parentId: rootDeptId);

        var profileRequest = new UpsertPersonProfileRequest
        {
            Email = "test@example.com",
            JobTitleId = jobTitleId,
            DepartmentId = childDeptId,
            Notes = "Test profile"
        };

        var upsertResp = await _client.PutAsJsonAsync($"/api/person-profiles/{person.Id}", profileRequest);
        Assert.Equal(HttpStatusCode.OK, upsertResp.StatusCode);

        var profile = await upsertResp.Content.ReadFromJsonAsync<PersonProfileInfo>();
        Assert.NotNull(profile);
        Assert.Equal("test@example.com", profile.Email);
        Assert.Equal(jobTitleId, profile.JobTitleId);
        Assert.Equal(childDeptId, profile.DepartmentId);
        Assert.Equal("Test profile", profile.Notes);
        // Resolved display fields
        Assert.NotNull(profile.JobTitleName);
        Assert.Contains(" / ", profile.DepartmentPath); // path includes both levels

        // Retrieve profile
        var getResp = await _client.GetAsync($"/api/person-profiles/{person.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var retrievedProfile = await getResp.Content.ReadFromJsonAsync<PersonProfileInfo>();
        Assert.NotNull(retrievedProfile);
        Assert.Equal(profile.Email, retrievedProfile.Email);
        Assert.Equal(profile.JobTitleId, retrievedProfile.JobTitleId);
        Assert.Equal(profile.DepartmentId, retrievedProfile.DepartmentId);
    }

    [Fact]
    public async Task UpsertPersonProfile_WithUnknownDepartmentId_Returns400()
    {
        var person = await CreatePersonAsync($"P-{Guid.NewGuid():N}"[..20]);
        var bogusDeptId = Guid.NewGuid();

        var profileRequest = new UpsertPersonProfileRequest
        {
            Email = "fkfail@example.com",
            DepartmentId = bogusDeptId,
        };

        var resp = await _client.PutAsJsonAsync($"/api/person-profiles/{person.Id}", profileRequest);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private async Task<Guid> CreateJobTitleAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/job-titles",
            new CreateJobTitleRequest { Name = name });
        resp.EnsureSuccessStatusCode();
        var jt = await resp.Content.ReadFromJsonAsync<JobTitleInfo>();
        return jt!.Id;
    }

    private async Task<Guid> CreateDepartmentAsync(string name, Guid? parentId)
    {
        var resp = await _client.PostAsJsonAsync("/api/departments",
            new CreateDepartmentRequest { Name = name, ParentDepartmentId = parentId });
        resp.EnsureSuccessStatusCode();
        var d = await resp.Content.ReadFromJsonAsync<DepartmentInfo>();
        return d!.Id;
    }

    [Fact]
    public async Task LinkUser_ToPersonProfile_Succeeds()
    {
        var person = await CreatePersonAsync($"P-{Guid.NewGuid():N}"[..20]);
        var userId = await DatabaseTestUtils.CreateTestUserAsync($"link-{Guid.NewGuid():N}@test.com"[..40]);

        var linkRequest = new LinkUserToPersonProfileRequest { UserId = userId };
        var linkResp = await _client.PostAsJsonAsync($"/api/person-profiles/{person.Id}/link", linkRequest);
        Assert.Equal(HttpStatusCode.NoContent, linkResp.StatusCode);

        // Verify link
        var profile = await (await _client.GetAsync($"/api/person-profiles/{person.Id}")).Content.ReadFromJsonAsync<PersonProfileInfo>();
        Assert.NotNull(profile);
        Assert.Equal(userId, profile.LinkedUserId);
    }

    [Fact]
    public async Task LinkUser_AlreadyLinked_Returns409()
    {
        var person1 = await CreatePersonAsync($"P1-{Guid.NewGuid():N}"[..20]);
        var person2 = await CreatePersonAsync($"P2-{Guid.NewGuid():N}"[..20]);
        var userId = await DatabaseTestUtils.CreateTestUserAsync($"already-{Guid.NewGuid():N}@test.com"[..40]);

        // Link user to first person
        await _client.PostAsJsonAsync($"/api/person-profiles/{person1.Id}/link", new LinkUserToPersonProfileRequest { UserId = userId });

        // Try to link same user to second person - should conflict
        var linkResp = await _client.PostAsJsonAsync($"/api/person-profiles/{person2.Id}/link", new LinkUserToPersonProfileRequest { UserId = userId });
        Assert.Equal(HttpStatusCode.Conflict, linkResp.StatusCode);
    }

    [Fact]
    public async Task UnlinkUser_FromPersonProfile_Succeeds()
    {
        var person = await CreatePersonAsync($"P-{Guid.NewGuid():N}"[..20]);
        var userId = await DatabaseTestUtils.CreateTestUserAsync($"unlink-{Guid.NewGuid():N}@test.com"[..40]);

        // Link user
        await _client.PostAsJsonAsync($"/api/person-profiles/{person.Id}/link", new LinkUserToPersonProfileRequest { UserId = userId });

        // Unlink user
        var unlinkResp = await _client.DeleteAsync($"/api/person-profiles/{person.Id}/link");
        Assert.Equal(HttpStatusCode.NoContent, unlinkResp.StatusCode);

        // Verify unlinked
        var profile = await (await _client.GetAsync($"/api/person-profiles/{person.Id}")).Content.ReadFromJsonAsync<PersonProfileInfo>();
        Assert.NotNull(profile);
        Assert.Null(profile.LinkedUserId);
    }

    [Fact]
    public async Task UnlinkUser_NotLinked_Returns404()
    {
        var person = await CreatePersonAsync($"P-{Guid.NewGuid():N}"[..20]);
        var unlinkResp = await _client.DeleteAsync($"/api/person-profiles/{person.Id}/link");
        Assert.Equal(HttpStatusCode.NotFound, unlinkResp.StatusCode);
    }

    [Fact]
    public async Task GetPersonJobTitles_ReturnsLabelsForGivenResources()
    {
        var p1 = await CreatePersonAsync($"BP1-{Guid.NewGuid():N}"[..20]);
        var p2 = await CreatePersonAsync($"BP2-{Guid.NewGuid():N}"[..20]);
        var jobTitleId = await CreateJobTitleAsync($"JT-{Guid.NewGuid():N}"[..20]);
        await _client.PutAsJsonAsync($"/api/person-profiles/{p1.Id}", new UpsertPersonProfileRequest { JobTitleId = jobTitleId });
        await _client.PutAsJsonAsync($"/api/person-profiles/{p2.Id}", new UpsertPersonProfileRequest { Email = "bp2@example.com" });

        // Include an unknown id — it should simply be omitted, not error.
        var resp = await _client.PostAsJsonAsync(
            "/api/person-profiles/job-titles", new[] { p1.Id, p2.Id, Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var labels = await resp.Content.ReadFromJsonAsync<List<PersonJobTitleInfo>>();
        Assert.NotNull(labels);
        Assert.Equal(2, labels.Count);
        Assert.Contains(labels, l => l.ResourceId == p1.Id && l.JobTitleName != null);
        Assert.Contains(labels, l => l.ResourceId == p2.Id && l.JobTitleName == null);
    }

    [Fact]
    public async Task GetPersonJobTitles_NoIds_ReturnsEmptyArray()
    {
        var resp = await _client.PostAsJsonAsync("/api/person-profiles/job-titles", Array.Empty<Guid>());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var labels = await resp.Content.ReadFromJsonAsync<List<PersonJobTitleInfo>>();
        Assert.NotNull(labels);
        Assert.Empty(labels);
    }
}
