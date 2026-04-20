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
        };
    }
}
