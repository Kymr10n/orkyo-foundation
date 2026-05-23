using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Repositories;

public static class CriteriaMapper
{
    public static CriterionInfo MapFromReader(NpgsqlDataReader reader)
    {
        var dataType = Enum.Parse<CriterionDataType>(reader.GetString("data_type"));

        List<string>? enumValues = null;
        var enumValuesJson = reader.GetNullableString("enum_values");
        if (enumValuesJson != null)
        {
            enumValues = JsonSerializer.Deserialize<List<string>>(enumValuesJson);
        }

        // resource_type_keys is a text[] aggregate (see CriteriaRepository.SelectColumns).
        var keysOrdinal = reader.GetOrdinal("resource_type_keys");
        var resourceTypeKeys = reader.IsDBNull(keysOrdinal)
            ? Array.Empty<string>()
            : reader.GetFieldValue<string[]>(keysOrdinal);

        return new CriterionInfo
        {
            Id = reader.GetGuid("id"),
            Name = reader.GetString("name"),
            Description = reader.GetNullableString("description"),
            DataType = dataType,
            EnumValues = enumValues,
            Unit = reader.GetNullableString("unit"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
            ApplicableToRequests = reader.GetBoolean("applicable_to_requests"),
            ResourceTypeKeys = resourceTypeKeys,
        };
    }
}
