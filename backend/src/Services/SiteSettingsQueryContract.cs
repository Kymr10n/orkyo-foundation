namespace Api.Services;

/// <summary>
/// SQL contract for the platform <c>site_settings</c> table.
///
/// Settings are platform-wide overrides on top of compiled defaults
/// (rate limits, brute-force thresholds, upload constraints, etc.).
/// The table layout is structurally identical in multi-tenant SaaS
/// and single-tenant Community deployments, so the read/upsert/delete
/// SQL belongs in foundation by default.
/// </summary>
public static class SiteSettingsQueryContract
{
    public const string KeyParameterName = "key";
    public const string ValueParameterName = "value";
    public const string CategoryParameterName = "category";

    public static string BuildSelectAllSettingsSql()
    {
        return "SELECT key, value FROM site_settings";
    }

    public static string BuildUpsertSettingSql()
    {
        return @"
            INSERT INTO site_settings (key, value, category, updated_at)
            VALUES (@key, @value, @category, NOW())
            ON CONFLICT (key) DO UPDATE SET
                value = @value,
                updated_at = NOW()";
    }

    public static string BuildDeleteSettingSql()
    {
        return "DELETE FROM site_settings WHERE key = @key";
    }
}
