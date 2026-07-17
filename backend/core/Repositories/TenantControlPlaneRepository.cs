using Api.Services;
using Npgsql;

namespace Api.Repositories;

public sealed class TenantControlPlaneRepository : ITenantControlPlaneRepository
{
    /// <summary>Column list every <see cref="TenantRecord"/> read/RETURNING uses, in <see cref="MapTenantRecord"/> ordinal order.</summary>
    private const string TenantProjection = "id, slug, display_name, status, db_identifier, owner_user_id, created_at";

    private readonly IDbConnectionFactory _connectionFactory;

    public TenantControlPlaneRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private static TenantRecord MapTenantRecord(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetGuid(5),
        reader.GetDateTime(6));

    public async Task<bool> OwnsActiveTenantAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        var count = await conn.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM tenants
            WHERE owner_user_id = @userId AND status != 'deleting'",
            p => p.AddWithValue("userId", userId), ct);
        return count > 0;
    }

    public async Task<bool> IsSlugTakenAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.ExecuteScalarAsync<object>(
            "SELECT id FROM tenants WHERE slug = @slug",
            p => p.AddWithValue("slug", slug), ct) is not null;
    }

    public async Task<TenantRecord> CreateTenantWithOwnerAsync(
        string slug, string displayName, string dbIdentifier, Guid ownerId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        TenantRecord tenant;
        await using (var insertCmd = new NpgsqlCommand($@"
            INSERT INTO tenants (slug, display_name, status, db_identifier, owner_user_id, created_at, updated_at)
            VALUES (@slug, @displayName, 'active', @dbIdentifier, @ownerId, NOW(), NOW())
            RETURNING {TenantProjection}", conn, tx))
        {
            insertCmd.Parameters.AddWithValue("slug", slug);
            insertCmd.Parameters.AddWithValue("displayName", displayName);
            insertCmd.Parameters.AddWithValue("dbIdentifier", dbIdentifier);
            insertCmd.Parameters.AddWithValue("ownerId", ownerId);
            await using var reader = await insertCmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            tenant = MapTenantRecord(reader);
        }

        await using (var membershipCmd = new NpgsqlCommand(@"
            INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
            VALUES (@userId, @tenantId, 'admin', 'active', NOW(), NOW())", conn, tx))
        {
            membershipCmd.Parameters.AddWithValue("userId", ownerId);
            membershipCmd.Parameters.AddWithValue("tenantId", tenant.Id);
            await membershipCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return tenant;
    }

    public async Task<TenantRecord?> GetByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {TenantProjection} FROM tenants WHERE id = @id",
            p => p.AddWithValue("id", tenantId),
            MapTenantRecord, ct);
    }

    public async Task<TenantRecord?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {TenantProjection} FROM tenants WHERE slug = @slug",
            p => p.AddWithValue("slug", slug),
            MapTenantRecord, ct);
    }

    public async Task<TenantRecord?> UpdateDisplayNameAsync(Guid tenantId, string displayName, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync($@"
            UPDATE tenants SET display_name = @displayName, updated_at = NOW()
            WHERE id = @tenantId
            RETURNING {TenantProjection}",
            p =>
            {
                p.AddWithValue("displayName", displayName);
                p.AddWithValue("tenantId", tenantId);
            },
            MapTenantRecord, ct);
    }

    public async Task<TenantOwnerStatus?> GetOwnerStatusAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(
            "SELECT owner_user_id, status FROM tenants WHERE id = @tenantId",
            p => p.AddWithValue("tenantId", tenantId),
            reader => new TenantOwnerStatus(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.GetString(1)), ct);
    }

    public async Task<TenantMembershipRoleStatus?> GetMembershipRoleStatusAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(@"
            SELECT role, status FROM tenant_memberships
            WHERE tenant_id = @tenantId AND user_id = @userId",
            p =>
            {
                p.AddWithValue("tenantId", tenantId);
                p.AddWithValue("userId", userId);
            },
            reader => new TenantMembershipRoleStatus(reader.GetString(0), reader.GetString(1)), ct);
    }

    public async Task<List<TenantMembershipRow>> GetUserMembershipsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(@"
            SELECT
                t.id, t.slug, t.display_name, t.status, t.owner_user_id,
                tm.role, tm.status, tm.created_at
            FROM tenant_memberships tm
            JOIN tenants t ON t.id = tm.tenant_id
            WHERE tm.user_id = @userId
            ORDER BY tm.created_at DESC",
            p => p.AddWithValue("userId", userId),
            reader => new TenantMembershipRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDateTime(7)), ct);
    }

    public async Task<TenantLeaveLookup> GetLeaveLookupAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        // Owner check, active-admin count, and the user's current active role in one round trip.
        var lookup = await conn.QuerySingleOrDefaultAsync(@"
            SELECT (SELECT owner_user_id FROM tenants WHERE id = @tenantId),
                   (SELECT COUNT(*) FROM tenant_memberships
                    WHERE tenant_id = @tenantId AND role = 'admin' AND status = 'active'),
                   (SELECT role FROM tenant_memberships
                    WHERE tenant_id = @tenantId AND user_id = @userId AND status = 'active')",
            p =>
            {
                p.AddWithValue("tenantId", tenantId);
                p.AddWithValue("userId", userId);
            },
            reader => new TenantLeaveLookup(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)), ct);
        // Scalar subselects always yield exactly one row; keep the compiler happy.
        return lookup ?? new TenantLeaveLookup(null, 0, null);
    }

    public async Task MarkDeletingAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(
            "UPDATE tenants SET status = 'deleting', updated_at = NOW() WHERE id = @tenantId",
            p => p.AddWithValue("tenantId", tenantId), ct);
    }

    public async Task MarkActiveAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(
            "UPDATE tenants SET status = 'active', updated_at = NOW() WHERE id = @tenantId",
            p => p.AddWithValue("tenantId", tenantId), ct);
    }

    public async Task TransferOwnershipAsync(Guid tenantId, Guid newOwnerId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            UPDATE tenants SET owner_user_id = @newOwnerId, updated_at = NOW()
            WHERE id = @tenantId",
            p =>
            {
                p.AddWithValue("newOwnerId", newOwnerId);
                p.AddWithValue("tenantId", tenantId);
            }, ct);
    }

    public async Task DeleteMembershipAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            DELETE FROM tenant_memberships
            WHERE tenant_id = @tenantId AND user_id = @userId",
            p =>
            {
                p.AddWithValue("tenantId", tenantId);
                p.AddWithValue("userId", userId);
            }, ct);
    }
}
