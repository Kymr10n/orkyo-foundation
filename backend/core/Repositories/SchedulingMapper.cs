using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Repositories;

public static class SchedulingMapper
{
    public static SchedulingSettingsInfo MapSettingsFromReader(NpgsqlDataReader reader)
    {
        return new SchedulingSettingsInfo
        {
            Id = reader.GetGuid("id"),
            SiteId = reader.GetGuid("site_id"),
            TimeZone = reader.GetString("time_zone"),
            WorkingHoursEnabled = reader.GetBoolean("working_hours_enabled"),
            WorkingDayStart = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(reader.GetOrdinal("working_day_start"))),
            WorkingDayEnd = TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(reader.GetOrdinal("working_day_end"))),
            WeekendsEnabled = reader.GetBoolean("weekends_enabled"),
            PublicHolidaysEnabled = reader.GetBoolean("public_holidays_enabled"),
            PublicHolidayRegion = reader.GetNullableString("public_holiday_region"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
        };
    }

    public static AvailabilityEventInfo MapAvailabilityEventFromReader(NpgsqlDataReader reader)
    {
        return new AvailabilityEventInfo
        {
            Id = reader.GetGuid("id"),
            SiteId = reader.GetGuid("site_id"),
            Title = reader.GetString("title"),
            Description = reader.GetNullableString("description"),
            EventType = EnumMapper.FromDbValue<AvailabilityEventType>(reader.GetString("event_type")),
            DefaultEffect = EnumMapper.FromDbValue<DefaultEffect>(reader.GetString("default_effect")),
            StartTs = reader.GetDateTime("start_ts"),
            EndTs = reader.GetDateTime("end_ts"),
            IsRecurring = reader.GetBoolean("is_recurring"),
            RecurrenceRule = reader.GetNullableString("recurrence_rule"),
            Enabled = reader.GetBoolean("enabled"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
        };
    }

    public static AvailabilityEventScopeInfo MapScopeFromReader(NpgsqlDataReader reader)
    {
        return new AvailabilityEventScopeInfo
        {
            Id = reader.GetGuid("id"),
            AvailabilityEventId = reader.GetGuid("availability_event_id"),
            TargetType = EnumMapper.FromDbValue<ScopeTargetType>(reader.GetString("target_type")),
            TargetId = reader.GetGuid("target_id"),
            Effect = EnumMapper.FromDbValue<ScopeEffect>(reader.GetString("effect")),
        };
    }

    public static ResourceAbsenceInfo MapResourceAbsenceFromReader(NpgsqlDataReader reader)
    {
        return new ResourceAbsenceInfo
        {
            Id = reader.GetGuid("id"),
            ResourceId = reader.GetGuid("resource_id"),
            AbsenceType = EnumMapper.FromDbValue<AbsenceType>(reader.GetString("absence_type")),
            Title = reader.GetString("title"),
            Notes = reader.GetNullableString("notes"),
            StartTs = reader.GetDateTime("start_ts"),
            EndTs = reader.GetDateTime("end_ts"),
            IsRecurring = reader.GetBoolean("is_recurring"),
            RecurrenceRule = reader.GetNullableString("recurrence_rule"),
            Enabled = reader.GetBoolean("enabled"),
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
        };
    }
}
