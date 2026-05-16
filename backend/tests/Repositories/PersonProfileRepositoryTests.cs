using System;
using System.Threading.Tasks;
using Api.Models;
using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

[Collection("Database collection")]
public class PersonProfileRepositoryTests
{
    private readonly IPersonProfileRepository _repo;

    public PersonProfileRepositoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IPersonProfileRepository>();
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
        var resourceId = Guid.NewGuid();
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
        var resourceId = Guid.NewGuid();
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
        var resourceId = Guid.NewGuid();
        await _repo.UpsertAsync(resourceId, new UpsertPersonProfileRequest());

        var userId = Guid.NewGuid();
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
        var resourceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _repo.UpsertAsync(resourceId, new UpsertPersonProfileRequest());
        await _repo.LinkUserAsync(resourceId, userId);

        var unlinked = await _repo.UnlinkUserAsync(resourceId);
        Assert.True(unlinked);

        var profile = await _repo.GetByResourceIdAsync(resourceId);
        Assert.NotNull(profile);
        Assert.Null(profile.LinkedUserId);
    }
}
