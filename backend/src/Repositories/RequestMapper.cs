using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Repositories;

public static class RequestMapper
{
    public static RequestInfo MapFromReader(NpgsqlDataReader reader)
    {
        return new RequestInfo
        {
            Id = reader.GetGuid("id"),
            Name = reader.GetString("name"),
            Description = reader.GetNullableString("description"),
            ParentRequestId = reader.GetNullableGuid("parent_request_id"),
            PlanningMode = EnumMapper.ParseEnum<PlanningMode>(reader.GetString("planning_mode")),
            SortOrder = reader.GetInt32("sort_order"),
            SpaceId = reader.GetNullableGuid("space_id"),
            RequestItemId = reader.GetNullableString("request_item_id"),
            StartTs = reader.GetNullableDateTime("start_ts"),
            EndTs = reader.GetNullableDateTime("end_ts"),
            EarliestStartTs = reader.GetNullableDateTime("earliest_start_ts"),
            LatestEndTs = reader.GetNullableDateTime("latest_end_ts"),
            MinimalDurationValue = reader.GetInt32("minimal_duration_value"),
            MinimalDurationUnit = EnumMapper.ParseEnum<DurationUnit>(reader.GetString("minimal_duration_unit")),
            ActualDurationValue = reader.GetNullableInt32("actual_duration_value"),
            ActualDurationUnit = reader.GetNullableString("actual_duration_unit") is { } actualUnit
                ? EnumMapper.ParseEnum<DurationUnit>(actualUnit)
                : null,
            Status = EnumMapper.FromDbValue<RequestStatus>(reader.GetString("status")),
            SchedulingSettingsApply = reader.GetBoolean("scheduling_settings_apply"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
        };
    }

    public static RequestRequirementInfo MapRequirementFromReader(NpgsqlDataReader reader)
    {
        return new RequestRequirementInfo
        {
            Id = reader.GetGuid("id"),
            RequestId = reader.GetGuid("request_id"),
            CriterionId = reader.GetGuid("criterion_id"),
            Value = JsonDocument.Parse(reader.GetString("value")).RootElement.Clone(),
            CreatedAt = reader.GetDateTime("created_at"),
        };
    }

    public static RequestRequirementInfo MapRequirementWithCriterionFromReader(NpgsqlDataReader reader)
    {
        // The JOIN query selects rr.* first, then c.*; resolve each alias by position
        // to avoid ambiguous column names (both tables have id, created_at, etc.).
        return new RequestRequirementInfo
        {
            Id = reader.GetGuid(0),
            RequestId = reader.GetGuid(1),
            CriterionId = reader.GetGuid(2),
            Value = JsonDocument.Parse(reader.GetString(3)).RootElement.Clone(),
            CreatedAt = reader.GetDateTime(4),
            Criterion = new CriterionBasicInfo
            {
                Id = reader.GetGuid(5),
                Name = reader.GetString(6),
                DataType = EnumMapper.ParseEnum<CriterionDataType>(reader.GetString(7)),
                Unit = reader.IsDBNull(8) ? null : reader.GetString(8),
                EnumValues = reader.IsDBNull(9) ? null : JsonSerializer.Deserialize<List<string>>(reader.GetString(9)),
            },
        };
    }

    public static CriterionBasicInfo MapCriterionFromReader(NpgsqlDataReader reader)
    {
        return new CriterionBasicInfo
        {
            Id = reader.GetGuid("id"),
            Name = reader.GetString("name"),
            DataType = EnumMapper.ParseEnum<CriterionDataType>(reader.GetString("data_type")),
            Unit = reader.GetNullableString("unit"),
            EnumValues = reader.GetNullableString("enum_values") is { } enumJson
                ? JsonSerializer.Deserialize<List<string>>(enumJson)
                : null,
        };
    }
}
