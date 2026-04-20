using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Repositories;

public static class SiteMapper
{
    public static SiteInfo MapFromReader(NpgsqlDataReader reader)
    {
        return new SiteInfo
        {
            Id = reader.GetGuid("id"),
            Code = reader.GetNullableString("code"),
            Name = reader.GetString("name"),
            Description = reader.GetNullableString("description"),
            Address = reader.GetNullableString("address"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
        };
    }
}
