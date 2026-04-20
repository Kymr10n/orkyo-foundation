using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Repositories;

public static class SpaceMapper
{
    public static SpaceInfo MapFromReader(NpgsqlDataReader reader)
    {
        var geometryJson = reader.GetNullableString("geometry");
        var propertiesJson = reader.GetNullableString("properties") ?? "{}";

        SpaceGeometry? geometry = null;
        if (geometryJson != null)
        {
            geometry = JsonSerializer.Deserialize<SpaceGeometry>(geometryJson);
        }

        Dictionary<string, object>? properties = null;
        if (propertiesJson != "{}")
        {
            properties = JsonSerializer.Deserialize<Dictionary<string, object>>(propertiesJson);
        }

        return new SpaceInfo
        {
            Id = reader.GetGuid("id"),
            SiteId = reader.GetGuid("site_id"),
            Name = reader.GetString("name"),
            Code = reader.GetNullableString("code"),
            Description = reader.GetNullableString("description"),
            IsPhysical = reader.GetBoolean("is_physical"),
            Geometry = geometry,
            Properties = properties,
            Capacity = reader.GetInt32("capacity"),
            GroupId = reader.GetNullableGuid("group_id"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
        };
    }
}
