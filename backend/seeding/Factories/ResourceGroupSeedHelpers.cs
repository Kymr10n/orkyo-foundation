using Npgsql;
using NpgsqlTypes;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// The one way a seed profile bulk-writes <c>resource_groups</c> and their members. The
/// machine, person and space factories each carried their own copy of these two COPY blocks,
/// the same drift <see cref="ResourceTypeSeedHelpers"/> already ended for types.
/// </summary>
public static class ResourceGroupSeedHelpers
{
    /// <summary>
    /// Writes one group per entry, with no description or colour and the entry's index as
    /// its display order. Returns the new ids in input order.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> CopyGroupsAsync(
        NpgsqlConnection conn, IReadOnlyList<(string Name, Guid ResourceTypeId)> groups)
    {
        var ids = new List<Guid>(groups.Count);
        var now = DateTime.UtcNow;

        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resource_groups (id, name, description, color, display_order, resource_type_id, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)");
        for (var i = 0; i < groups.Count; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            await writer.StartRowAsync();
            await writer.WriteAsync(id, NpgsqlDbType.Uuid);
            await writer.WriteAsync(groups[i].Name, NpgsqlDbType.Varchar);
            await writer.WriteNullAsync();                              // description
            await writer.WriteNullAsync();                              // color
            await writer.WriteAsync(i, NpgsqlDbType.Integer);           // display_order
            await writer.WriteAsync(groups[i].ResourceTypeId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
        }
        await writer.CompleteAsync();
        return ids;
    }

    /// <summary>Writes the memberships and returns how many it wrote.</summary>
    public static async Task<int> CopyMembersAsync(
        NpgsqlConnection conn, IEnumerable<(Guid GroupId, Guid ResourceId, Guid ResourceTypeId)> members)
    {
        var count = 0;
        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resource_group_members (resource_group_id, resource_id, resource_type_id) " +
            "FROM STDIN (FORMAT BINARY)");
        foreach (var (groupId, resourceId, typeId) in members)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(groupId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(resourceId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(typeId, NpgsqlDbType.Uuid);
            count++;
        }
        await writer.CompleteAsync();
        return count;
    }
}
