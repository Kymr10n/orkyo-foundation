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

        // Create profile
        var profileRequest = new UpsertPersonProfileRequest
        {
            Email = "test@example.com",
            JobTitle = "Developer",
            Department = "Engineering",
            Notes = "Test profile"
        };

        var upsertResp = await _client.PutAsJsonAsync($"/api/person-profiles/{person.Id}", profileRequest);
        Assert.Equal(HttpStatusCode.OK, upsertResp.StatusCode);

        var profile = await upsertResp.Content.ReadFromJsonAsync<PersonProfileInfo>();
        Assert.NotNull(profile);
        Assert.Equal("test@example.com", profile.Email);
        Assert.Equal("Developer", profile.JobTitle);
        Assert.Equal("Engineering", profile.Department);
        Assert.Equal("Test profile", profile.Notes);

        // Retrieve profile
        var getResp = await _client.GetAsync($"/api/person-profiles/{person.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var retrievedProfile = await getResp.Content.ReadFromJsonAsync<PersonProfileInfo>();
        Assert.NotNull(retrievedProfile);
        Assert.Equal(profile.Email, retrievedProfile.Email);
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
}
