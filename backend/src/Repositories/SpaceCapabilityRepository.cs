using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class SpaceCapabilityRepository : ISpaceCapabilityRepository
{
    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public SpaceCapabilityRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    private static async Task EnsureSpaceInSiteAsync(NpgsqlConnection conn, Guid siteId, Guid spaceId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM spaces WHERE id = @spaceId AND site_id = @siteId", conn);
        cmd.Parameters.AddWithValue("spaceId", spaceId);
        cmd.Parameters.AddWithValue("siteId", siteId);
        if (await cmd.ExecuteScalarAsync() == null)
            throw new InvalidOperationException("Space not found");
    }

    public async Task<List<SpaceCapabilityInfo>> GetAllAsync(Guid siteId, Guid spaceId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Verify space exists and belongs to site
        await EnsureSpaceInSiteAsync(conn, siteId, spaceId);

        // Get capabilities with criterion details
        await using var cmd = new NpgsqlCommand(@"
            SELECT 
                sc.id,
                sc.space_id,
                sc.criterion_id,
                sc.value,
                sc.created_at,
                sc.updated_at,
                c.name as criterion_name,
                c.data_type as criterion_type,
                c.unit as criterion_unit
            FROM space_capabilities sc
            JOIN criteria c ON sc.criterion_id = c.id
            WHERE sc.space_id = @spaceId
            ORDER BY c.name",
            conn);
        cmd.Parameters.AddWithValue("spaceId", spaceId);

        await using var reader = await cmd.ExecuteReaderAsync();
        var capabilities = new List<SpaceCapabilityInfo>();

        while (await reader.ReadAsync())
        {
            capabilities.Add(SpaceCapabilityMapper.MapFromReader(reader, includeCriterion: true));
        }

        return capabilities;
    }

    public async Task<SpaceCapabilityInfo> CreateAsync(Guid siteId, Guid spaceId, Guid criterionId, object value)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Verify space and criterion exist
        await EnsureSpaceInSiteAsync(conn, siteId, spaceId);

        if (!await DbQueryHelper.ExistsAsync(conn, "criteria", criterionId))
            throw new InvalidOperationException("Criterion not found");

        // Insert capability
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO space_capabilities (space_id, criterion_id, value)
            VALUES (@spaceId, @criterionId, @value::jsonb)
            RETURNING id, space_id, criterion_id, value, created_at, updated_at",
            conn);
        cmd.Parameters.AddWithValue("spaceId", spaceId);
        cmd.Parameters.AddWithValue("criterionId", criterionId);
        cmd.Parameters.AddWithValue("value", JsonSerializer.Serialize(value));

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException("Failed to create capability");
            }

            return SpaceCapabilityMapper.MapFromReader(reader, includeCriterion: false);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
        {
            throw new InvalidOperationException("This criterion already has a value for this space");
        }
    }

    public async Task<SpaceCapabilityInfo?> UpdateAsync(Guid siteId, Guid spaceId, Guid capabilityId, object value)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Update capability
        await using var cmd = new NpgsqlCommand(@"
            UPDATE space_capabilities
            SET value = @value::jsonb,
                updated_at = NOW()
            WHERE id = @capabilityId 
              AND space_id = @spaceId
              AND space_id IN (SELECT id FROM spaces WHERE site_id = @siteId)
            RETURNING id, space_id, criterion_id, value, created_at, updated_at",
            conn);
        cmd.Parameters.AddWithValue("capabilityId", capabilityId);
        cmd.Parameters.AddWithValue("spaceId", spaceId);
        cmd.Parameters.AddWithValue("siteId", siteId);
        cmd.Parameters.AddWithValue("value", JsonSerializer.Serialize(value));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return SpaceCapabilityMapper.MapFromReader(reader, includeCriterion: false);
    }

    public async Task<bool> DeleteAsync(Guid siteId, Guid spaceId, Guid capabilityId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM space_capabilities
            WHERE id = @capabilityId 
              AND space_id = @spaceId
              AND space_id IN (SELECT id FROM spaces WHERE site_id = @siteId)",
            conn);
        cmd.Parameters.AddWithValue("capabilityId", capabilityId);
        cmd.Parameters.AddWithValue("spaceId", spaceId);
        cmd.Parameters.AddWithValue("siteId", siteId);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }
}
