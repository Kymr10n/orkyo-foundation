using System.Text.Json.Serialization;

namespace Api.Helpers;

/// <summary>
/// Generic utility for mapping enums to/from database string values.
/// Supports JsonStringEnumMemberName (.NET 9+), JsonPropertyName attributes,
/// and default string conversion.
/// </summary>
public static class EnumMapper
{
    /// <summary>
    /// Converts an enum value to its database string representation.
    /// Uses JsonStringEnumMemberName or JsonPropertyName attribute if present,
    /// otherwise uses enum name in lowercase.
    /// </summary>
    public static string ToDbValue<T>(T enumValue) where T : Enum
    {
        var type = typeof(T);
        var memberInfo = type.GetMember(enumValue.ToString()).FirstOrDefault();

        if (memberInfo != null)
        {
            // Prefer .NET 9+ JsonStringEnumMemberName
            var memberNameAttr = memberInfo.GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), false)
                .FirstOrDefault() as JsonStringEnumMemberNameAttribute;

            if (memberNameAttr != null)
            {
                return memberNameAttr.Name;
            }

            // Fallback to JsonPropertyName for backward compatibility
            var propNameAttr = memberInfo.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                .FirstOrDefault() as JsonPropertyNameAttribute;

            if (propNameAttr != null)
            {
                return propNameAttr.Name;
            }
        }

        // Fallback to enum name in lowercase
        return enumValue.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Converts a database string value to its corresponding enum value.
    /// Matches against JsonStringEnumMemberName, JsonPropertyName attributes,
    /// or enum names (case-insensitive).
    /// </summary>
    public static T FromDbValue<T>(string dbValue) where T : Enum
    {
        var type = typeof(T);

        foreach (var field in type.GetFields())
        {
            if (!field.IsLiteral) continue;

            // Check JsonStringEnumMemberName attribute (.NET 9+)
            var memberNameAttr = field.GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), false)
                .FirstOrDefault() as JsonStringEnumMemberNameAttribute;

            if (memberNameAttr != null && memberNameAttr.Name.Equals(dbValue, StringComparison.OrdinalIgnoreCase))
            {
                return (T)field.GetValue(null)!;
            }

            // Check JsonPropertyName attribute (backward compatibility)
            var propNameAttr = field.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                .FirstOrDefault() as JsonPropertyNameAttribute;

            if (propNameAttr != null && propNameAttr.Name.Equals(dbValue, StringComparison.OrdinalIgnoreCase))
            {
                return (T)field.GetValue(null)!;
            }

            // Check enum name (case-insensitive)
            if (field.Name.Equals(dbValue, StringComparison.OrdinalIgnoreCase))
            {
                return (T)field.GetValue(null)!;
            }
        }

        throw new ArgumentException($"Cannot convert '{dbValue}' to {typeof(T).Name}");
    }

    /// <summary>
    /// Parses an enum from a string value using Enum.Parse with ignoreCase=true.
    /// Use this for enums that are stored as their string names in the database.
    /// </summary>
    public static T ParseEnum<T>(string value) where T : struct, Enum
    {
        return Enum.Parse<T>(value, ignoreCase: true);
    }

    /// <summary>
    /// Convenience: convert a DB planning_mode string to the PlanningMode enum.
    /// </summary>
    public static Models.PlanningMode ToPlanningMode(string dbValue)
        => FromDbValue<Models.PlanningMode>(dbValue);
}
