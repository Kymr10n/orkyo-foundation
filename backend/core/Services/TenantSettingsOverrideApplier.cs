using System.Globalization;
using System.Reflection;
using Api.Models;

namespace Api.Services;

/// <summary>
/// Pure value-conversion + override application logic for <see cref="TenantSettings"/>.
///
/// Given a dictionary of dot-separated DB keys → string values, materializes a
/// new <see cref="TenantSettings"/> instance with overrides applied on top of
/// compiled defaults. Unknown keys are ignored. Conversion failures fall back
/// to the default value for that property.
/// </summary>
public static class TenantSettingsOverrideApplier
{
    private static readonly TenantSettings _defaults = new();

    // Cache backing-field info to avoid per-call reflection. TenantSettings is a record
    // with init-only properties, so we set the compiler-generated backing field directly.
    private static readonly Dictionary<string, FieldInfo?> _backingFields = TenantSettingsKeyPolicy.SettingsProperties
        .ToDictionary(
            p => p.Name,
            p => typeof(TenantSettings).GetField(
                $"<{p.Name}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic));

    /// <summary>The compiled defaults instance (immutable, shared).</summary>
    public static TenantSettings Defaults => _defaults;

    /// <summary>
    /// Apply DB overrides on top of compiled defaults into a new <see cref="TenantSettings"/>.
    /// Returns the shared <see cref="Defaults"/> instance when no overrides apply.
    /// </summary>
    public static TenantSettings Apply(IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0) return _defaults;

        var resolved = new Dictionary<string, object>();
        foreach (var (dbKey, dbValue) in overrides)
        {
            if (!TenantSettingsKeyPolicy.KeyToProperty.TryGetValue(dbKey, out var prop)) continue;
            var converted = ConvertValue(prop.PropertyType, dbValue);
            if (converted != null)
                resolved[prop.Name] = converted;
        }

        if (resolved.Count == 0) return _defaults;

        var instance = new TenantSettings();
        foreach (var p in TenantSettingsKeyPolicy.SettingsProperties)
        {
            var v = resolved.TryGetValue(p.Name, out var overrideVal)
                ? overrideVal
                : p.GetValue(_defaults);
            if (_backingFields.TryGetValue(p.Name, out var field))
                field?.SetValue(instance, v);
        }
        return instance;
    }

    /// <summary>
    /// Convert a string value to the target type using invariant culture.
    /// Returns null if the value cannot be converted (caller falls back to default).
    /// </summary>
    public static object? ConvertValue(Type targetType, string value)
    {
        try
        {
            if (targetType == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(string)) return value;
            return null;
        }
        catch
        {
            return null;
        }
    }
}
