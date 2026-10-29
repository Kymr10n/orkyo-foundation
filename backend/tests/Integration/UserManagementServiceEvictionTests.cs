using Api.Constants;
using Api.Services;
using Api.Services.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// A status write must evict what the request pipeline cached about the user: the role in each
/// tenant and the principal of each linked subject. Without it a disabled user kept both for the
/// cache TTL (the role on tenant routes, the principal on the tenant-less ones).
/// </summary>
[Trait(TestCategories.Name, TestCategories.Database)]
[Collection(PostgresCollection.Name)]
public sealed class UserManagementServiceEvictionTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SetGlobalStatus_Disabled_EvictsTheRoleInEveryTenantAndThePrincipal()
    {
        var userId = Guid.NewGuid();
        var subject = $"kc-{userId:N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await SeedUserWithMembershipsAsync(userId, subject, tenantA, tenantB);

        var cache = new SingleFlightCache(new MemoryCache(new MemoryCacheOptions()));
        var evictor = new IdentityCacheEvictor(cache, NullLogger<IdentityCacheEvictor>.Instance);
        var service = new UserManagementService(
            fixture.CreateConnectionFactory(),
            new Mock<ITenantUserService>().Object,
            NullLogger<UserManagementService>.Instance,
            evictor);
        var ttl = TimeSpan.FromMinutes(5);
        cache.Set(IdentityCacheKeys.Role(userId, tenantA), "admin", ttl);
        cache.Set(IdentityCacheKeys.Role(userId, tenantB), "viewer", ttl);
        cache.Set(IdentityCacheKeys.Principal(subject), "principal", ttl);
        var stranger = Guid.NewGuid();
        cache.Set(IdentityCacheKeys.Role(stranger, tenantA), "admin", ttl);

        await service.SetGlobalStatusAsync(userId, UserStatusConstants.Disabled);

        cache.TryGet<string>(IdentityCacheKeys.Role(userId, tenantA), out _).Should().BeFalse();
        cache.TryGet<string>(IdentityCacheKeys.Role(userId, tenantB), out _).Should().BeFalse();
        cache.TryGet<string>(IdentityCacheKeys.Principal(subject), out _).Should().BeFalse();
        cache.TryGet<string>(IdentityCacheKeys.Role(stranger, tenantA), out _).Should().BeTrue("another user's entry is untouched");
    }

    private async Task SeedUserWithMembershipsAsync(Guid userId, string subject, params Guid[] tenantIds)
    {
        await using var conn = await fixture.OpenControlPlaneConnectionAsync();
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, 'Evict Me', 'active'); " +
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) VALUES (@iid, @id, 'keycloak', @sub, @email)", conn))
        {
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("iid", Guid.NewGuid());
            cmd.Parameters.AddWithValue("email", $"{userId:N}@example.com");
            cmd.Parameters.AddWithValue("sub", subject);
            await cmd.ExecuteNonQueryAsync();
        }
        foreach (var tenantId in tenantIds)
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO tenants (id, slug, display_name, status, db_identifier) VALUES (@t, @slug, 'Evict Tenant', 'active', @db); " +
                "INSERT INTO tenant_memberships (user_id, tenant_id, role, status) VALUES (@u, @t, 'viewer', 'active')", conn);
            cmd.Parameters.AddWithValue("t", tenantId);
            cmd.Parameters.AddWithValue("slug", $"ev-{tenantId:N}"[..20]);
            cmd.Parameters.AddWithValue("db", $"tenant_ev_{tenantId:N}");
            cmd.Parameters.AddWithValue("u", userId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
