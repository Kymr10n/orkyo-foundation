namespace Api.Constants;

/// <summary>
/// Keys of the resource types seeded by migration 1300. Since 1800 the "space" type is an
/// ordinary tenant type (renamable, deletable), so membership here does not imply the type
/// still exists or is a system type. Tenants define additional types at runtime, so this set
/// is NOT the universe of valid resource type keys — resolve arbitrary keys through
/// <c>IResourceTypeRepository</c> instead.
/// </summary>
public static class ResourceTypeKeys
{
    public const string Space = "space";
    public const string Person = "person";
    public const string Tool = "tool";

    /// <summary>System type keys, in canonical (alphabetical) order.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Person,
        Space,
        Tool,
    };

    /// <summary>True when the key belongs to a system type. Does not imply the key is invalid
    /// when false — user-defined types exist only in the database.</summary>
    public static bool IsKnown(string? key) => key is not null && All.Contains(key);
}
