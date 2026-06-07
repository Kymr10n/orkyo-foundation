using Api.Constants;
using Api.Repositories;
using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// DB-backed tests for space↔group membership on <see cref="ResourceGroupMemberRepository"/>.
/// Verifies the 1:1 space rule end-to-end: assigning a space to a group moves it out of any
/// other group (app-layer move semantics in SetMembersAsync), and a direct duplicate insert
/// is rejected by the trg_space_single_group guard (migration 1530).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ResourceGroupMemberRepositoryIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ResourceGroupMemberRepositoryIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SetMembersAsync_MovesSpace_OutOfPriorGroup()
    {
        var (members, resources, groups) = Build();
        var groupA = await groups.CreateAsync("space", $"A-{Guid.NewGuid():N}", null, 100, null, null);
        var groupB = await groups.CreateAsync("space", $"B-{Guid.NewGuid():N}", null, 100, null, null);
        var space = await CreateSpaceAsync(resources);

        try
        {
            await members.SetMembersAsync(groupA.Id, new[] { space });
            (await members.GetGroupIdsForResourceAsync(space)).Should().BeEquivalentTo(new[] { groupA.Id });

            // Assigning to B must move it (1:1), not duplicate.
            await members.SetMembersAsync(groupB.Id, new[] { space });
            (await members.GetGroupIdsForResourceAsync(space)).Should().BeEquivalentTo(new[] { groupB.Id });
        }
        finally
        {
            await DeleteGroupAsync(groupA.Id);
            await DeleteGroupAsync(groupB.Id);
        }
    }

    [Fact]
    public async Task DirectDuplicateSpaceMembership_IsRejectedByTrigger()
    {
        var (members, resources, groups) = Build();
        var groupA = await groups.CreateAsync("space", $"A-{Guid.NewGuid():N}", null, 100, null, null);
        var groupB = await groups.CreateAsync("space", $"B-{Guid.NewGuid():N}", null, 100, null, null);
        var space = await CreateSpaceAsync(resources);

        try
        {
            await members.SetMembersAsync(groupA.Id, new[] { space });

            // A raw insert into a second group (bypassing the move) must trip the guard.
            var act = async () => await InsertMemberRawAsync(groupB.Id, space);
            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");
        }
        finally
        {
            await DeleteGroupAsync(groupA.Id);
            await DeleteGroupAsync(groupB.Id);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private (ResourceGroupMemberRepository members, ResourceRepository resources, ResourceGroupRepository groups) Build()
    {
        var factory = _fixture.CreateConnectionFactory();
        var org = new OrgContext
        {
            OrgId = Guid.NewGuid(),
            OrgSlug = "test-tenant",
            DbConnectionString = _fixture.TestTenantConnectionString,
        };
        return (
            new ResourceGroupMemberRepository(org, factory),
            new ResourceRepository(org, factory),
            new ResourceGroupRepository(org, factory));
    }

    private async Task<Guid> CreateSpaceAsync(ResourceRepository resources)
    {
        var spaceTypeId = await SpaceTypeIdAsync();
        var resource = await resources.CreateAsync(
            spaceTypeId, "space", $"S-{Guid.NewGuid():N}", null, null, AllocationModes.Exclusive, 100);
        return resource.Id;
    }

    private async Task<Guid> SpaceTypeIdAsync()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT id FROM resource_types WHERE key = 'space'", conn);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task InsertMemberRawAsync(Guid groupId, Guid resourceId)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO resource_group_members (resource_group_id, resource_id, resource_type_id) " +
            "SELECT @g, r.id, r.resource_type_id FROM resources r WHERE r.id = @r", conn);
        cmd.Parameters.AddWithValue("g", groupId);
        cmd.Parameters.AddWithValue("r", resourceId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task DeleteGroupAsync(Guid groupId)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM resource_groups WHERE id = @g", conn);
        cmd.Parameters.AddWithValue("g", groupId);
        await cmd.ExecuteNonQueryAsync();
    }
}
