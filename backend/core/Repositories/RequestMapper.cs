using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Repositories;

public static class RequestMapper
{
    public static RequestInfo MapFromReader(NpgsqlDataReader reader)
    {
        var startTs = reader.GetNullableDateTime("start_ts");
        var endTs = reader.GetNullableDateTime("end_ts");
        // Status is EFFECTIVE: the active lifecycle (new → in_progress → done) is derived from the
        // schedule vs now; cancelled/deferred stay as stored. See RequestStatusCalculator.
        var storedStatus = EnumMapper.FromDbValue<RequestStatus>(reader.GetString("status"));

        return new RequestInfo
        {
            Id = reader.GetGuid("id"),
            Name = reader.GetString("name"),
            Description = reader.GetNullableString("description"),
            ParentRequestId = reader.GetNullableGuid("parent_request_id"),
            PlanningMode = EnumMapper.ParseEnum<PlanningMode>(reader.GetString("planning_mode")),
            SortOrder = reader.GetInt32("sort_order"),
            SiteId = reader.GetNullableGuid("site_id"),
            RequestItemId = reader.GetNullableString("request_item_id"),
            Assignments = ParseAssignments(reader.GetString("assignments")),
            TargetResourceTypeKeys = reader.GetStringArray("target_resource_type_keys"),
            Icon = reader.GetNullableString("icon"),
            StartTs = startTs,
            EndTs = endTs,
            EarliestStartTs = reader.GetNullableDateTime("earliest_start_ts"),
            LatestEndTs = reader.GetNullableDateTime("latest_end_ts"),
            MinimalDurationValue = reader.GetInt32("minimal_duration_value"),
            MinimalDurationUnit = EnumMapper.ParseEnum<DurationUnit>(reader.GetString("minimal_duration_unit")),
            ActualDurationValue = reader.GetNullableInt32("actual_duration_value"),
            ActualDurationUnit = reader.GetNullableString("actual_duration_unit") is { } actualUnit
                ? EnumMapper.ParseEnum<DurationUnit>(actualUnit)
                : null,
            Status = RequestStatusCalculator.Effective(storedStatus, startTs, endTs, DateTime.UtcNow),
            // FromDbValue, not ParseEnum: the DB strings follow JsonStringEnumMemberName, and
            // "k_of_n" does not match the member name KOfN the way "leaf" matches Leaf.
            PredecessorLogic = EnumMapper.FromDbValue<PredecessorLogic>(reader.GetString("predecessor_logic")),
            PredecessorLogicK = reader.GetNullableInt32("predecessor_logic_k"),
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
            Value = reader.GetJsonElement("value"),
            Operator = reader.GetNullableString("operator"),
            AllowedValues = reader.GetNullableJsonElement("allowed_values"),
            CreatedAt = reader.GetDateTime("created_at"),
        };
    }

    public static RequestRequirementInfo MapRequirementWithCriterionFromReader(NpgsqlDataReader reader)
    {
        // The criterion columns are aliased in the JOIN (see RequestSql) because both tables
        // carry id and created_at. Aliasing is what lets this read by name: a positional read
        // is silently wrong the moment the SELECT list is reordered.
        return new RequestRequirementInfo
        {
            Id = reader.GetGuid("id"),
            RequestId = reader.GetGuid("request_id"),
            CriterionId = reader.GetGuid("criterion_id"),
            Value = reader.GetJsonElement("value"),
            CreatedAt = reader.GetDateTime("created_at"),
            Operator = reader.GetNullableString("operator"),
            AllowedValues = reader.GetNullableJsonElement("allowed_values"),
            Criterion = new CriterionBasicInfo
            {
                Id = reader.GetGuid("criterion_pk"),
                Name = reader.GetString("criterion_name"),
                DataType = EnumMapper.ParseEnum<CriterionDataType>(reader.GetString("criterion_data_type")),
                Unit = reader.GetNullableString("criterion_unit"),
                EnumValues = reader.GetNullableString("criterion_enum_values") is { } enumValues
                    ? JsonSerializer.Deserialize<List<string>>(enumValues)
                    : null,
                ResourceTypeKeys = reader.GetStringArray("criterion_resource_type_keys"),
            },
        };
    }

    private static readonly JsonSerializerOptions AssignmentOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static IReadOnlyList<ResourceAssignmentInfo> ParseAssignments(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "[]")
            return Array.Empty<ResourceAssignmentInfo>();
        return JsonSerializer.Deserialize<List<ResourceAssignmentInfo>>(json, AssignmentOptions)!
            .AsReadOnly();
    }
}
