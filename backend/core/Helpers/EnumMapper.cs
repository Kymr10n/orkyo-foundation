using System.Reflection;
using System.Text.Json.Serialization;

namespace Api.Helpers;

/// <summary>
/// Generic utility for mapping enums to/from database string values.
/// Supports JsonStringEnumMemberName (.NET 9+), JsonPropertyName attributes,
/// and default string conversion.
/// </summary>
/// <remarks>
/// The attribute reflection runs once per enum type (<see cref="Map{T}"/>); mappers call these
/// per row, and reading custom attributes on every call was the cost of each read.
/// </remarks>
public static class EnumMapper
{
    /// <summary>
    /// Converts an enum value to its database string representation.
    /// Uses JsonStringEnumMemberName or JsonPropertyName attribute if present,
    /// otherwise uses enum name in lowercase.
    /// </summary>
    public static string ToDbValue<T>(T enumValue) where T : Enum
        => Map<T>.ToDb.TryGetValue(enumValue, out var name)
            ? name
            : enumValue.ToString().ToLowerInvariant();

    /// <summary>
    /// Converts a database string value to its corresponding enum value.
    /// Matches against JsonStringEnumMemberName, JsonPropertyName attributes,
    /// or enum names (case-insensitive).
    /// </summary>
    public static T FromDbValue<T>(string dbValue) where T : Enum
        => Map<T>.FromDb.TryGetValue(dbValue, out var value)
            ? value
            : throw new ArgumentException($"Cannot convert '{dbValue}' to {typeof(T).Name}");

    private static class Map<T> where T : Enum
    {
        public static readonly Dictionary<T, string> ToDb = [];
        public static readonly Dictionary<string, T> FromDb = new(StringComparer.OrdinalIgnoreCase);

        static Map()
        {
            foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = (T)field.GetValue(null)!;
                // Prefer .NET 9+ JsonStringEnumMemberName, then JsonPropertyName for backward
                // compatibility, then the lowercase member name.
                var memberName = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name;
                var propertyName = field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                ToDb.TryAdd(value, memberName ?? propertyName ?? field.Name.ToLowerInvariant());

                // Field order decides a clash, as the per-field scan it replaces did.
                if (memberName is not null) FromDb.TryAdd(memberName, value);
                if (propertyName is not null) FromDb.TryAdd(propertyName, value);
                FromDb.TryAdd(field.Name, value);
            }
        }
    }
}
