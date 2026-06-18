using System;
using System.Threading.Tasks;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

[Collection("Database collection")]
public class PersonProfileRepositoryTests
{
    private readonly IPersonProfileRepository _repo;
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public PersonProfileRepositoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IPersonProfileRepository>();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    /// <summary>
    /// person_profiles.resource_id is a FK to resources(id) — every Upsert/Link
    /// test needs a real resource row first. Returns the inserted id so the test
    /// can use it as the profile key.
    /// </summary>
    private async Task<Guid> SeedPersonResourceAsync()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO resources (resource_type_id, name, allocation_mode)
              SELECT id, @name, 'Exclusive' FROM resource_types WHERE key = 'person'
              RETURNING id", conn);
        cmd.Parameters.AddWithValue("name", $"Test Person {Guid.NewGuid():N}"[..30]);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// person_profiles.linked_user_id is a FK to users(id) — link tests need a
    /// real user row first.
    /// </summary>
    private async Task<Guid> SeedUserAsync()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO users (email)
              VALUES (@email)
              RETURNING id", conn);
        cmd.Parameters.AddWithValue("email", $"linkuser-{Guid.NewGuid():N}@test.local");
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task GetByResourceId_ReturnsNull_WhenNotExists()
    {
        var result = await _repo.GetByResourceIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByLinkedUserId_ReturnsNull_WhenNotExists()
    {
        var result = await _repo.GetByLinkedUserIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task Upsert_CreatesNewProfile()
    {
        var resourceId = await SeedPersonResourceAsync();
        var request = new UpsertPersonProfileRequest
        {
            Email = "new@example.com"
        };
        var created = await _repo.UpsertAsync(resourceId, request);

        Assert.NotNull(created);
        Assert.Equal(resourceId, created.ResourceId);
        Assert.Equal(request.Email, created.Email);

        var retrieved = await _repo.GetByResourceIdAsync(resourceId);
        Assert.NotNull(retrieved);
        Assert.Equal(created.Email, retrieved.Email);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingProfile()
    {
        var resourceId = await SeedPersonResourceAsync();
        var request1 = new UpsertPersonProfileRequest
        {
            Email = "old@example.com"
        };
        await _repo.UpsertAsync(resourceId, request1);

        var request2 = new UpsertPersonProfileRequest
        {
            Email = "updated@example.com"
        };
        var updated = await _repo.UpsertAsync(resourceId, request2);

        Assert.NotNull(updated);
        Assert.Equal("updated@example.com", updated.Email);
    }

    [Fact]
    public async Task LinkUser_AssociatesUserWithProfile()
    {
        var resourceId = await SeedPersonResourceAsync();
        await _repo.UpsertAsync(resourceId, new UpsertPersonProfileRequest());

        var userId = await SeedUserAsync();
        var linked = await _repo.LinkUserAsync(resourceId, userId);
        Assert.True(linked);

        var profile = await _repo.GetByResourceIdAsync(resourceId);
        Assert.NotNull(profile);
        Assert.Equal(userId, profile.LinkedUserId);

        var byUser = await _repo.GetByLinkedUserIdAsync(userId);
        Assert.NotNull(byUser);
        Assert.Equal(resourceId, byUser.ResourceId);
    }

    [Fact]
    public async Task UnlinkUser_RemovesAssociation()
    {
        var resourceId = await SeedPersonResourceAsync();
        var userId = await SeedUserAsync();

        await _repo.UpsertAsync(resourceId, new UpsertPersonProfileRequest());
        await _repo.LinkUserAsync(resourceId, userId);

        var unlinked = await _repo.UnlinkUserAsync(resourceId);
        Assert.True(unlinked);

        var profile = await _repo.GetByResourceIdAsync(resourceId);
        Assert.NotNull(profile);
        Assert.Null(profile.LinkedUserId);
    }

    [Fact]
    public async Task GetByResourceIds_ReturnsEmpty_ForEmptyInput()
    {
        var result = await _repo.GetByResourceIdsAsync([]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByResourceIds_ReturnsFullProfiles_OnlyForResourcesWithRows()
    {
        var r1 = await SeedPersonResourceAsync();
        var r2 = await SeedPersonResourceAsync();
        await _repo.UpsertAsync(r1, new UpsertPersonProfileRequest { Email = "batch-a@example.com", Notes = "secret a" });
        await _repo.UpsertAsync(r2, new UpsertPersonProfileRequest { Email = "batch-b@example.com" });

        // r3 is a person resource with NO profile row; the random id matches nothing at all.
        var r3 = await SeedPersonResourceAsync();

        var result = await _repo.GetByResourceIdsAsync([r1, r2, r3, Guid.NewGuid()]);

        Assert.Equal(2, result.Count);
        var a = Assert.Single(result, p => p.ResourceId == r1);
        Assert.Equal("batch-a@example.com", a.Email);
        // Notes are encrypted at rest; the bulk path must decrypt like the single lookup.
        Assert.Equal("secret a", a.Notes);
        Assert.Contains(result, p => p.ResourceId == r2);
        Assert.DoesNotContain(result, p => p.ResourceId == r3);
    }

    [Fact]
    public async Task GetJobTitles_ReturnsEmpty_ForEmptyInput()
    {
        var result = await _repo.GetJobTitlesByResourceIdsAsync([]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetJobTitles_ReturnsRowsForResourcesWithProfiles()
    {
        var r1 = await SeedPersonResourceAsync();
        var r2 = await SeedPersonResourceAsync();
        await _repo.UpsertAsync(r1, new UpsertPersonProfileRequest { Email = "bulk-a@example.com" });
        await _repo.UpsertAsync(r2, new UpsertPersonProfileRequest { Email = "bulk-b@example.com" });

        // r3 is a person resource with NO profile row; the random id matches nothing at all.
        var r3 = await SeedPersonResourceAsync();

        var result = await _repo.GetJobTitlesByResourceIdsAsync([r1, r2, r3, Guid.NewGuid()]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, j => j.ResourceId == r1);
        Assert.Contains(result, j => j.ResourceId == r2);
        Assert.DoesNotContain(result, j => j.ResourceId == r3);
        // No job title assigned → null label; real title resolution is covered in the endpoint test.
        Assert.All(result, j => Assert.Null(j.JobTitleName));
    }
}
