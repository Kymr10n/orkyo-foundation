using System.Text.Json;
using Npgsql;

namespace Api.Repositories;

public static class SpaceCapabilityMapper
{
    public static SpaceCapabilityInfo MapFromReader(NpgsqlDataReader reader, bool includeCriterion = true)
    {
        object? value = null;
        if (!reader.IsDBNull(3))
        {
            value = JsonSerializer.Deserialize<JsonElement>(reader.GetString(3));
        }

        CriterionMetadata? criterion = null;
        if (includeCriterion)
        {
            criterion = new CriterionMetadata
            {
                Id = reader.GetGuid(2),
                Name = reader.GetString(6),
                DataType = reader.GetString(7),
                Unit = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
        }

        return new SpaceCapabilityInfo
        {
            Id = reader.GetGuid(0),
            SpaceId = reader.GetGuid(1),
            CriterionId = reader.GetGuid(2),
            Value = value,
            CreatedAt = reader.GetDateTime(4),
            UpdatedAt = reader.GetDateTime(5),
            Criterion = criterion
        };
    }

}
