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

    public static OffTimeInfo MapOffTimeFromReader(NpgsqlDataReader reader)
    {
        return new OffTimeInfo
        {
            Id = reader.GetGuid("id"),
            SiteId = reader.GetGuid("site_id"),
            Title = reader.GetString("title"),
            Type = EnumMapper.ParseEnum<OffTimeType>(reader.GetString("type")),
            AppliesToAllSpaces = reader.GetBoolean("applies_to_all_spaces"),
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
