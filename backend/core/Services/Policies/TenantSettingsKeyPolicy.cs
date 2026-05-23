using System.Globalization;
using System.Reflection;
using Api.Models;

namespace Api.Services;

/// <summary>
/// Pure naming/key policy for <see cref="TenantSettings"/>.
///
/// Maps <c>TenantSettings</c> CLR property names to dot-separated
/// category-prefixed DB keys (e.g. <c>PasswordMinLength</c> →
/// <c>security.password_min_length</c>) and exposes pre-computed
/// reflection lookups so callers do not pay reflection cost per request.
/// </summary>
public static class TenantSettingsKeyPolicy
{
    private static readonly PropertyInfo[] _settingsProps =
        typeof(TenantSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static readonly Dictionary<string, PropertyInfo> _keyToProperty = BuildKeyToProperty();

    private static readonly Dictionary<string, string> _defaultsMap = BuildDefaultsMap();

    /// <summary>All public instance properties of <see cref="TenantSettings"/>.</summary>
    public static IReadOnlyList<PropertyInfo> SettingsProperties => _settingsProps;

    /// <summary>Map of dot-separated DB key → property info (case-insensitive).</summary>
    public static IReadOnlyDictionary<string, PropertyInfo> KeyToProperty => _keyToProperty;

    /// <summary>Map of dot-separated DB key → default value as invariant string.</summary>
    public static IReadOnlyDictionary<string, string> DefaultsMap => _defaultsMap;

    /// <summary>
    /// Convert a property name to its dot-separated DB key.
    /// e.g. <c>PasswordMinLength</c> → <c>security.password_min_length</c>.
    /// </summary>
    public static string PropertyToKey(string propertyName)
    {
        var category = GetCategory(propertyName);
        var snakeCase = ToSnakeCase(propertyName);
        return $"{category}.{snakeCase}";
    }

    /// <summary>
    /// Resolve the category prefix for a given property name.
    /// Convention based on property-name prefixes.
    /// </summary>
    public static string GetCategory(string propertyName)
    {
        if (propertyName.StartsWith("Password") || propertyName.StartsWith("BruteForce") || propertyName.StartsWith("RateLimit"))
            return "security";
        if (propertyName.StartsWith("Invitation"))
            return "invitations";
        if (propertyName.StartsWith("Upload"))
            return "uploads";
        if (propertyName.StartsWith("Search"))
            return "search";
        if (propertyName.StartsWith("Branding"))
            return "branding";
        if (propertyName.StartsWith("AutoSchedule"))
            return "scheduling";
        return "general";
    }

    /// <summary>
    /// Convert a PascalCase / mixed-underscore property name to snake_case.
    /// Underscores already present are preserved.
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        var chars = new List<char>(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_')
            {
                chars.Add('_');
                continue;
            }
            if (char.IsUpper(c) && i > 0 && name[i - 1] != '_')
            {
                chars.Add('_');
            }
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }

    private static Dictionary<string, PropertyInfo> BuildKeyToProperty()
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in _settingsProps)
        {
            map[PropertyToKey(prop.Name)] = prop;
        }
        return map;
    }

    private static Dictionary<string, string> BuildDefaultsMap()
    {
        var defaults = new TenantSettings();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in _settingsProps)
        {
            var key = PropertyToKey(prop.Name);
            var value = prop.GetValue(defaults);
            map[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
        return map;
    }
}
