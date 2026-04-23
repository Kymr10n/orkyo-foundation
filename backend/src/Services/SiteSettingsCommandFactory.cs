using Npgsql;

namespace Api.Services;

public static class SiteSettingsCommandFactory
{
    public static NpgsqlCommand CreateSelectAllSettingsCommand(NpgsqlConnection connection)
    {
        return new NpgsqlCommand(SiteSettingsQueryContract.BuildSelectAllSettingsSql(), connection);
    }

    public static NpgsqlCommand CreateUpsertSettingCommand(
        NpgsqlConnection connection, string key, string value, string category)
    {
        var command = new NpgsqlCommand(SiteSettingsQueryContract.BuildUpsertSettingSql(), connection);
        command.Parameters.AddWithValue(SiteSettingsQueryContract.KeyParameterName, key);
        command.Parameters.AddWithValue(SiteSettingsQueryContract.ValueParameterName, value);
        command.Parameters.AddWithValue(SiteSettingsQueryContract.CategoryParameterName, category);
        return command;
    }

    public static NpgsqlCommand CreateDeleteSettingCommand(NpgsqlConnection connection, string key)
    {
        var command = new NpgsqlCommand(SiteSettingsQueryContract.BuildDeleteSettingSql(), connection);
        command.Parameters.AddWithValue(SiteSettingsQueryContract.KeyParameterName, key);
        return command;
    }
}

public static class SiteSettingsReaderFlow
{
    /// <summary>
    /// Drain an open <see cref="NpgsqlDataReader"/> produced by
    /// <see cref="SiteSettingsCommandFactory.CreateSelectAllSettingsCommand"/>
    /// into a case-insensitive key/value dictionary.
    /// </summary>
    public static async Task<Dictionary<string, string>> ReadAllSettingsAsync(NpgsqlDataReader reader)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }
        return settings;
    }
}
