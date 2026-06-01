namespace Api.Constants;

/// <summary>
/// Entity-type discriminator stored in <c>templates.entity_type</c> and used by
/// the preset import/export paths. Distinct from <see cref="ResourceTypeKeys"/>:
/// the values <c>space</c> coincide by accident, not by shared ownership — these
/// live in a different column and may diverge.
/// </summary>
public static class TemplateEntityTypes
{
    public const string Request = "request";
    public const string Space = "space";
    public const string Group = "group";

    /// <summary>Known entity types, in canonical (space → group → request) order.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Space,
        Group,
        Request,
    };

    public static bool IsKnown(string? entityType) =>
        entityType is not null && All.Contains(entityType);
}
